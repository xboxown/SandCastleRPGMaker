/*
 * Reading a number from text with only plain arithmetic. libnix's strtod and
 * scanf use FMOVECR, an FPU instruction the 68040 and 68060 don't have in
 * hardware (it needs 68060.library, and crashes with #8000000B without it) -
 * so on the Amiga the player reads its numbers (game.json) with this one.
 */
#include <ctype.h>

double sc_strtod(const char *text, char **end)
{
    const char *p = text;
    while (isspace((unsigned char)*p)) p++;
    int negative = 0;
    if (*p == '-' || *p == '+') negative = *p++ == '-';
    double value = 0;
    int digits = 0;
    while (*p >= '0' && *p <= '9') { value = value * 10.0 + (*p++ - '0'); digits++; }
    if (*p == '.')
    {
        p++;
        double scale = 0.1;
        while (*p >= '0' && *p <= '9') { value += (*p++ - '0') * scale; scale *= 0.1; digits++; }
    }
    if (!digits) { if (end) *end = (char *)text; return 0; }
    if (*p == 'e' || *p == 'E')
    {
        const char *mark = p++;
        int expNegative = 0, exponent = 0, expDigits = 0;
        if (*p == '-' || *p == '+') expNegative = *p++ == '-';
        while (*p >= '0' && *p <= '9') { if (exponent < 400) exponent = exponent * 10 + (*p - '0'); p++; expDigits++; }
        if (!expDigits) p = mark;
        else
        {
            double ten = 10.0, factor = 1.0;
            while (exponent) { if (exponent & 1) factor *= ten; ten *= ten; exponent >>= 1; }
            value = expNegative ? value / factor : value * factor;
        }
    }
    if (end) *end = (char *)p;
    return negative ? -value : value;
}
