using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace SandcastleRpgMaker.AI;

/// <summary>What to draw.</summary>
/// <param name="Prompt">The words: what it is and how it looks.</param>
/// <param name="NegativePrompt">What it must not have (blurry, text...).</param>
/// <param name="Width">Picture width, a multiple of 8 (512 is what the model knows best).</param>
/// <param name="Height">Picture height, a multiple of 8.</param>
/// <param name="Steps">How many times the picture is refined (20-30).</param>
/// <param name="Guidance">How closely it follows the words (7-8).</param>
/// <param name="Seed">The same seed and words make the same picture; -1: a new one each time.</param>
/// <param name="StartImage">Redraw this picture instead of starting from noise (RGBA, Width x Height); null: from words only.</param>
/// <param name="Strength">How much of the start picture is redrawn (0.1 keeps it, 1 ignores it).</param>
public sealed record GenerateRequest(string Prompt, string NegativePrompt = "", int Width = 512, int Height = 512, int Steps = 25,
    double Guidance = 7.5, long Seed = -1, byte[]? StartImage = null, double Strength = 0.6);

/// <summary>The picture made: RGBA bytes, row by row.</summary>
public sealed record GeneratedImage(int Width, int Height, byte[] Rgba, long Seed);

/// <summary>
/// Stable Diffusion 1.5 (ONNX, fp16) on the graphics card via DirectML: the CLIP text encoder reads the words, the UNet
/// takes the noise away step by step (Euler, classifier-free guidance), the VAE decoder turns it into a picture.
/// The model folder holds text_encoder/, unet/, vae_decoder/, vae_encoder/ and tokenizer/ (diffusers' ONNX layout).
/// </summary>
public sealed class StableDiffusion : IDisposable
{
    private const float LatentScale = 0.18215f;
    private const int TrainSteps = 1000;

    private readonly string _folder;
    private readonly SessionOptions _options;
    private readonly ClipTokenizer _tokenizer;
    private readonly InferenceSession _textEncoder, _unet, _vaeDecoder;
    private InferenceSession? _vaeEncoder;
    private readonly float[] _sigmas = new float[TrainSteps]; // the noise level of each of the 1000 training steps

    /// <summary>Whether the graphics card runs it (false: the processor - very slow).</summary>
    public bool OnGraphicsCard { get; }

    public StableDiffusion(string modelFolder, bool useGraphicsCard = true)
    {
        _folder = modelFolder;
        _options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        if (useGraphicsCard)
        {
            try
            {
                // DirectML wants no memory pattern and one model run at a time.
                _options.EnableMemoryPattern = false;
                _options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                _options.AppendExecutionProvider_DML(0);
                OnGraphicsCard = true;
            }
            catch (Exception) { OnGraphicsCard = false; }
        }
        _tokenizer = new ClipTokenizer(Path.Combine(modelFolder, "tokenizer"));
        _textEncoder = new InferenceSession(Path.Combine(modelFolder, "text_encoder", "model.onnx"), _options);
        _unet = new InferenceSession(Path.Combine(modelFolder, "unet", "model.onnx"), _options);
        _vaeDecoder = new InferenceSession(Path.Combine(modelFolder, "vae_decoder", "model.onnx"), _options);

        // scaled_linear betas 0.00085 .. 0.012 (Stable Diffusion 1.5's scheduler_config.json)
        double start = Math.Sqrt(0.00085), end = Math.Sqrt(0.012), alphaProduct = 1;
        for (int t = 0; t < TrainSteps; t++)
        {
            double beta = Math.Pow(start + (end - start) * t / (TrainSteps - 1), 2);
            alphaProduct *= 1 - beta;
            _sigmas[t] = (float)Math.Sqrt((1 - alphaProduct) / alphaProduct);
        }
    }

    /// <summary>Whether a folder looks like a usable model (the parts this class loads).</summary>
    public static bool IsModelFolder(string folder) =>
        File.Exists(Path.Combine(folder, "tokenizer", "vocab.json")) && File.Exists(Path.Combine(folder, "tokenizer", "merges.txt"))
        && File.Exists(Path.Combine(folder, "text_encoder", "model.onnx")) && File.Exists(Path.Combine(folder, "unet", "model.onnx"))
        && File.Exists(Path.Combine(folder, "vae_decoder", "model.onnx"));

    /// <summary>Draws a picture. progress gets the step done (1..steps); cancel stops between steps.</summary>
    public GeneratedImage Generate(GenerateRequest request, IProgress<int>? progress = null, CancellationToken cancel = default)
    {
        int width = Math.Max(64, request.Width / 8 * 8), height = Math.Max(64, request.Height / 8 * 8);
        int lw = width / 8, lh = height / 8, latentSize = 4 * lw * lh;
        int steps = Math.Clamp(request.Steps, 1, 150);
        long seed = request.Seed >= 0 ? request.Seed : Random.Shared.NextInt64(0, int.MaxValue);
        var random = new Random((int)(seed & 0x7FFFFFFF));

        // The words: [nothing / what not to draw] then [the prompt], read together as a batch of two.
        float[] uncond = EncodeText(request.NegativePrompt ?? ""), cond = EncodeText(request.Prompt ?? "");
        var words = new float[uncond.Length * 2];
        uncond.CopyTo(words, 0);
        cond.CopyTo(words, uncond.Length);

        // The schedule: noise levels from the highest down to 0 (timesteps evenly spaced 999 .. 0).
        var timesteps = new float[steps];
        var sigmas = new float[steps + 1];
        for (int i = 0; i < steps; i++)
        {
            timesteps[i] = steps == 1 ? TrainSteps - 1 : (TrainSteps - 1) * (1 - (float)i / (steps - 1));
            sigmas[i] = SigmaAt(timesteps[i]);
        }
        sigmas[steps] = 0;

        var latents = new float[latentSize];
        int first = 0;
        if (request.StartImage is { } startImage && startImage.Length == width * height * 4)
        {
            // Redraw: start from the picture's own latents with the noise of a later step.
            first = Math.Clamp(steps - (int)Math.Round(steps * Math.Clamp(request.Strength, 0.01, 1.0)), 0, steps - 1);
            var start = EncodeImage(startImage, width, height);
            for (int i = 0; i < latentSize; i++) latents[i] = start[i] + Gaussian(random) * sigmas[first];
        }
        else
            for (int i = 0; i < latentSize; i++) latents[i] = Gaussian(random) * sigmas[0];

        for (int s = first; s < steps; s++)
        {
            cancel.ThrowIfCancellationRequested();
            float sigma = sigmas[s], scale = 1f / MathF.Sqrt(sigma * sigma + 1);
            var input = new float[latentSize * 2];
            for (int i = 0; i < latentSize; i++) input[i] = input[i + latentSize] = latents[i] * scale;
            float[] noise = RunUnet(input, timesteps[s], words, lw, lh);
            // Euler: epsilon prediction, guided - nothing + guidance x (prompt - nothing)
            float guidance = (float)request.Guidance, dt = sigmas[s + 1] - sigma;
            for (int i = 0; i < latentSize; i++)
            {
                float eps = noise[i] + guidance * (noise[i + latentSize] - noise[i]);
                latents[i] += eps * dt;
            }
            progress?.Report(s + 1);
        }
        return new GeneratedImage(width, height, DecodeLatents(latents, lw, lh), seed);
    }

    private float SigmaAt(float t)
    {
        int low = (int)MathF.Floor(t), high = Math.Min(low + 1, TrainSteps - 1);
        float w = t - low;
        // diffusers interpolates the sigmas linearly between training steps
        return _sigmas[low] * (1 - w) + _sigmas[high] * w;
    }

    private static float Gaussian(Random random)
    {
        double u1 = 1.0 - random.NextDouble(), u2 = random.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
    }

    // ---------- the three models ----------

    private float[] EncodeText(string text)
    {
        int[] ids = _tokenizer.Encode(text);
        var meta = _textEncoder.InputMetadata.First();
        NamedOnnxValue input = meta.Value.ElementDataType == TensorElementType.Int64
            ? NamedOnnxValue.CreateFromTensor(meta.Key, new DenseTensor<long>(ids.Select(i => (long)i).ToArray(), new[] { 1, ClipTokenizer.Length }))
            : NamedOnnxValue.CreateFromTensor(meta.Key, new DenseTensor<int>(ids, new[] { 1, ClipTokenizer.Length }));
        using var results = _textEncoder.Run(new[] { input });
        return Floats(results.FirstOrDefault(r => r.Name == "last_hidden_state") ?? results.First());
    }

    private float[] RunUnet(float[] sample, float timestep, float[] words, int lw, int lh)
    {
        var inputs = new List<NamedOnnxValue>();
        foreach (var (name, meta) in _unet.InputMetadata)
        {
            if (name.Contains("sample") && !name.Contains("hidden")) inputs.Add(Tensor(name, meta, sample, new[] { 2, 4, lh, lw }));
            else if (name.Contains("hidden")) inputs.Add(Tensor(name, meta, words, new[] { 2, ClipTokenizer.Length, words.Length / 2 / ClipTokenizer.Length }));
            else if (name.Contains("timestep"))
            {
                int count = meta.Dimensions.Length == 0 ? 0 : meta.Dimensions[0] is 2 or -1 ? 2 : 1;
                var values = Enumerable.Repeat(timestep, Math.Max(count, 1)).ToArray();
                int[] dims = count == 0 ? Array.Empty<int>() : new[] { values.Length };
                inputs.Add(meta.ElementDataType == TensorElementType.Int64
                    ? NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(values.Select(v => (long)Math.Round(v)).ToArray(), dims))
                    : Tensor(name, meta, values, dims));
            }
        }
        using var results = _unet.Run(inputs);
        return Floats(results.First());
    }

    private byte[] DecodeLatents(float[] latents, int lw, int lh)
    {
        var scaled = latents.Select(v => v / LatentScale).ToArray();
        var meta = _vaeDecoder.InputMetadata.First();
        using var results = _vaeDecoder.Run(new[] { Tensor(meta.Key, meta.Value, scaled, new[] { 1, 4, lh, lw }) });
        float[] pixels = Floats(results.First());
        int width = lw * 8, height = lh * 8, plane = width * height;
        var rgba = new byte[plane * 4];
        for (int i = 0; i < plane; i++)
        {
            for (int c = 0; c < 3; c++) rgba[i * 4 + c] = (byte)Math.Clamp((pixels[c * plane + i] / 2 + 0.5f) * 255f + 0.5f, 0, 255);
            rgba[i * 4 + 3] = 255;
        }
        return rgba;
    }

    private float[] EncodeImage(byte[] rgba, int width, int height)
    {
        _vaeEncoder ??= new InferenceSession(Path.Combine(_folder, "vae_encoder", "model.onnx"), _options);
        int plane = width * height;
        var pixels = new float[plane * 3];
        for (int i = 0; i < plane; i++)
            for (int c = 0; c < 3; c++) pixels[c * plane + i] = rgba[i * 4 + c] / 127.5f - 1f;
        var meta = _vaeEncoder.InputMetadata.First();
        using var results = _vaeEncoder.Run(new[] { Tensor(meta.Key, meta.Value, pixels, new[] { 1, 3, height, width }) });
        return Floats(results.First()).Select(v => v * LatentScale).ToArray();
    }

    // ---------- fp16 / fp32 ----------

    private static NamedOnnxValue Tensor(string name, NodeMetadata meta, float[] values, int[] dims) =>
        meta.ElementDataType == TensorElementType.Float16
            ? NamedOnnxValue.CreateFromTensor(name, new DenseTensor<Float16>(values.Select(v => (Float16)v).ToArray(), dims))
            : NamedOnnxValue.CreateFromTensor(name, new DenseTensor<float>(values, dims));

    private static float[] Floats(DisposableNamedOnnxValue value) => value.Value switch
    {
        Tensor<float> f => f.ToArray(),
        Tensor<Float16> h => h.Select(v => (float)v).ToArray(),
        _ => throw new InvalidDataException($"The model gave {value.Name} in a kind of numbers this doesn't read.")
    };

    public void Dispose()
    {
        _textEncoder.Dispose();
        _unet.Dispose();
        _vaeDecoder.Dispose();
        _vaeEncoder?.Dispose();
        _options.Dispose();
    }
}
