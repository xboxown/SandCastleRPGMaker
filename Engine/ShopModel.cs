namespace SandcastleRpgMaker.Engine;

/// <summary>
/// A merchant's shop (the Shop event command), as Ultima VII's merchants talk: a line of the merchant's own and the
/// player's choices, each leading to another line and choices (Weapons > Melee > the daggers and swords he has), the
/// goods of a choice offered as its last choices; a price asked, yes or no - or, as Serpent Isle's merchants, a haggle
/// down to the lowest price he'll take. He may buy things from the party too. Every word said is the designer's
/// (Texts): nothing is fixed in the engine. The command keeps it as JSON (its "Shop" parameter) - the stock left too.
/// </summary>
public class ShopDefinition
{
    /// <summary>Who talks (a Show Text's speaker: "Self", "Player", or a character's id).</summary>
    public string SpeakerId { get; set; } = "Self";
    /// <summary>The money it's paid in: an object (a Money object - gold coins).</summary>
    public int CurrencyObjectId { get; set; } = -1;
    /// <summary>What the merchant says first, and the player's choices from there.</summary>
    public ShopNode Root { get; set; } = new() { Line = "What wouldst thou buy?" };
    /// <summary>What the merchant buys from the party, and what he pays.</summary>
    public List<ShopBuy> Buys { get; set; } = new();
    /// <summary>Serpent Isle's haggling: the player may offer less.</summary>
    public bool Haggle { get; set; }
    /// <summary>How many offers before he'll haggle no more.</summary>
    public int HaggleTries { get; set; } = 3;
    /// <summary>The lowest he'll go, as a percent of his price.</summary>
    public int LowestPercent { get; set; } = 70;
    public ShopTexts Texts { get; set; } = new();
}

/// <summary>A step of the talk: the player's choice that leads here (Label), what the merchant says then (Line), and the choices after it - more steps, and the goods of this one.</summary>
public class ShopNode
{
    public string Label { get; set; } = "Weapons";
    public string Line { get; set; } = "I sell melee or missile weapons.";
    public List<ShopNode> Children { get; set; } = new();
    public List<ShopItem> Items { get; set; } = new();
}

/// <summary>Goods for sale: what, how many (or never running out), its price - and, if set, its own price line.</summary>
public class ShopItem
{
    public ReferencedItemCategory Category { get; set; } = ReferencedItemCategory.Object;
    public int ItemId { get; set; } = -1;
    public bool Infinite { get; set; } = true;
    public int Quantity { get; set; } = 1;
    public int Price { get; set; } = 10;
    /// <summary>Its own price line ({item}, {price}, {money}); empty: the shop's.</summary>
    public string PriceLine { get; set; } = string.Empty;
    /// <summary>In play: how many have been sold (a save keeps it).</summary>
    public int SoldRuntime { get; set; }
}

/// <summary>What the merchant buys from the party, and what he pays for one.</summary>
public class ShopBuy
{
    public ReferencedItemCategory Category { get; set; } = ReferencedItemCategory.Object;
    public int ItemId { get; set; } = -1;
    public int Price { get; set; } = 5;
}

/// <summary>Everything said in a shop - {item} the thing's name, {price} the price, {money} the money's name.</summary>
public class ShopTexts
{
    public string PriceLine { get; set; } = "Perfect! The {item} costs {price} {money}. Art thou interested?";
    public string Yes { get; set; } = "Yes";
    public string No { get; set; } = "No";
    public string HaggleChoice { get; set; } = "Haggle";
    public string Agreed { get; set; } = "Done! Here is thy {item}.";
    public string Declined { get; set; } = "As thou wishest.";
    public string NotEnoughMoney { get; set; } = "Thou hast not enough {money} for that.";
    public string OutOfStock { get; set; } = "I no longer have any {item}.";
    public string CantCarry { get; set; } = "Thou canst not carry any more.";
    public string Back { get; set; } = "Something else";
    public string Leave { get; set; } = "Nothing more";
    public string Farewell { get; set; } = "Fare thee well.";
    // Selling to the merchant
    public string SellChoice { get; set; } = "I would sell something";
    public string SellLine { get; set; } = "What wouldst thou sell?";
    public string SellOffer { get; set; } = "I will give thee {price} {money} for thy {item}. Agreed?";
    public string SellAgreed { get; set; } = "Done.";
    public string NothingToSell { get; set; } = "Thou hast nothing I want.";
    // Haggling (Serpent Isle)
    public string HagglePrompt { get; set; } = "How much wilt thou pay for the {item}?";
    public string HaggleTooLow { get; set; } = "Thou dost insult me! {price} {money} is my price.";
    public string HaggleCounter { get; set; } = "I cannot go so low. How about {price} {money}?";
    public string HaggleAccept { get; set; } = "Very well - {price} {money} it is.";
    public string HaggleGiveUp { get; set; } = "I will haggle with thee no more! {price} {money}, or nothing.";

    /// <summary>The words with {item}, {price} and {money} filled in.</summary>
    public static string Fill(string text, string item, int price, string money) =>
        text.Replace("{item}", item).Replace("{price}", price.ToString()).Replace("{money}", money);
}

/// <summary>Serpent Isle's haggle, the same in every engine: the player's offer against the merchant's asking price.</summary>
public static class ShopHaggle
{
    public enum Answer { Accept, TooLow, Counter, GiveUp }

    /// <summary>
    /// An offer: at or above what he asks - he takes his asking price; below his lowest - refused; else he meets the
    /// player half way (rounded up), never below his lowest - and once the counter is within one of the offer, he
    /// takes the offer. Each refusal or counter is a try; when the tries are used up he names his last price.
    /// </summary>
    public static (Answer Answer, int Price) Offer(int basePrice, int asking, int offer, int lowestPercent, int triesUsed, int tries)
    {
        int lowest = Math.Max(1, (int)Math.Ceiling(basePrice * Math.Clamp(lowestPercent, 1, 100) / 100.0));
        if (offer >= asking) return (Answer.Accept, asking);
        bool last = triesUsed + 1 >= Math.Max(1, tries);
        if (offer < lowest) return (last ? Answer.GiveUp : Answer.TooLow, asking);
        int counter = Math.Max(lowest, (asking + offer + 1) / 2);
        if (counter <= offer + 1) return (Answer.Accept, offer);
        return (last ? Answer.GiveUp : Answer.Counter, counter);
    }
}
