using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BulkRename.Core;

public static class RenameEngine
{
    public static string Preview(
        string fileName, RenameOptions options, int sequence, string? directory = null,
        FileDateTimes? dates = null, DateTime? currentDate = null)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(options);

        var extension = Path.GetExtension(fileName);
        var stem = extension.Length == 0 ? fileName : fileName[..^extension.Length];
        var target = options.RegexIncludeExtension ? fileName : stem;

        if (!string.IsNullOrEmpty(options.RegexPattern))
        {
            target = Regex.Replace(
                target,
                options.RegexPattern,
                options.RegexReplacement,
                options.RegexIgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None,
                TimeSpan.FromSeconds(2));
        }

        if (!options.RegexIncludeExtension)
        {
            stem = target;
        }
        else
        {
            extension = Path.GetExtension(target);
            stem = extension.Length == 0 ? target : target[..^extension.Length];
        }

        if (!string.IsNullOrEmpty(options.ReplaceText))
        {
            var comparison = options.ReplaceIgnoreCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            stem = ReplaceText(stem, options.ReplaceText, options.ReplaceWith, comparison, options.ReplaceFirstOnly);
        }

        stem = RemoveParts(stem, options);

        stem = options.CaseMode switch
        {
            "UPPERCASE" => stem.ToUpperInvariant(),
            "lowercase" => stem.ToLowerInvariant(),
            "Title Case" => ToTitleCase(stem),
            _ => stem
        };

        stem = options.NameMode switch
        {
            "Fixed" => options.NameValue,
            "Prefix" => options.NameValue + stem,
            "Suffix" => stem + options.NameValue,
            _ => stem
        };

        if (!string.IsNullOrEmpty(options.InsertText))
        {
            stem = stem.Insert(Math.Clamp(options.InsertPosition, 0, stem.Length), options.InsertText);
        }

        stem = options.Prefix + stem + options.Suffix;
        stem = AppendFolderName(stem, options, directory);

        if (options.NumberMode != "None")
        {
            long val;
            if (options.NumberBreak > 0)
            {
                var cycle = (sequence - 1) % options.NumberBreak;
                val = (long)options.NumberStart + cycle * options.NumberIncrement;
            }
            else
            {
                val = (long)options.NumberStart + (long)(sequence - 1) * options.NumberIncrement;
            }

            var number = FormatNumber(val, options);

            stem = options.NumberMode switch
            {
                "Prefix" => number + options.NumberSeparator + stem,
                "Suffix" => stem + options.NumberSeparator + number,
                "Pre+Suffix" or "Prefix+Suffix" => number + options.NumberSeparator + stem + options.NumberSeparator + number,
                "Insert" => stem.Insert(Math.Clamp(options.NumberAt, 0, stem.Length), number + options.NumberSeparator),
                _ => stem
            };
        }

        stem = AddDate(stem, options, dates, currentDate);

        extension = options.ExtensionMode switch
        {
            "UPPERCASE" => extension.ToUpperInvariant(),
            "lowercase" => extension.ToLowerInvariant(),
            "Title" => extension.Length > 1
                ? "." + char.ToUpperInvariant(extension[1]) + extension[2..].ToLowerInvariant()
                : extension,
            "Fixed" => options.ExtensionText.Length == 0 ? extension : "." + options.ExtensionText.TrimStart('.'),
            "Extra" => options.ExtensionText.Length == 0 ? extension : extension + "." + options.ExtensionText.TrimStart('.'),
            "Remove" => string.Empty,
            _ => extension
        };

        return stem + extension;
    }

    private static string AddDate(string stem, RenameOptions options, FileDateTimes? dates, DateTime? currentDate)
    {
        if (!options.DateEnabled || (options.DateMode == "None" && options.DateInsertPosition == 0))
        {
            return stem;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(options.DateInsertPosition);

        var date = options.DateType switch
        {
            "Created" => dates?.Created ?? throw new ArgumentException("Creation date is unavailable."),
            "Modified" => dates?.Modified ?? throw new ArgumentException("Modification date is unavailable."),
            "Accessed" => dates?.Accessed ?? throw new ArgumentException("Access date is unavailable."),
            "Current" => currentDate ?? DateTime.Now,
            _ => throw new ArgumentException($"Unknown date type: {options.DateType}")
        };
        if (date == DateTime.MinValue)
        {
            throw new ArgumentException($"The {options.DateType} date is unavailable.");
        }

        date = date.AddDays(options.DateOffsetDays);
        string formatted;
        if (options.DateFormat == "Custom")
        {
            if (string.IsNullOrEmpty(options.DateCustomFormat))
            {
                throw new FormatException("Enter a custom date format, for example %Y-%m-%d.");
            }

            formatted = FormatCustomDate(date, options.DateCustomFormat);
        }
        else
        {
            var segments = options.DateFormat switch
            {
                "DMY" => new[] { "dd", "MM", "y" },
                "MDY" => new[] { "MM", "dd", "y" },
                "YMD" => new[] { "y", "MM", "dd" },
                "YDM" => new[] { "y", "dd", "MM" },
                "MYD" => new[] { "MM", "y", "dd" },
                "DYM" => new[] { "dd", "y", "MM" },
                "HM" => new[] { "HH", "mm" },
                "HMS" => new[] { "HH", "mm", "ss" },
                "DMYHMS" => new[] { "dd", "MM", "y", "HH", "mm", "ss" },
                "MDYHMS" => new[] { "MM", "dd", "y", "HH", "mm", "ss" },
                "YMDHMS" => new[] { "y", "MM", "dd", "HH", "mm", "ss" },
                _ => throw new FormatException($"Unknown date format: {options.DateFormat}")
            };
            formatted = string.Join(options.DateSegmentSeparator, segments.Select(segment =>
                date.ToString(segment == "y" ? options.DateCentury ? "yyyy" : "yy" : segment, CultureInfo.InvariantCulture)));
        }

        if (options.DateInsertPosition > 0)
        {
            return stem.Insert(Math.Min(options.DateInsertPosition - 1, stem.Length), formatted + options.DateSeparator);
        }

        return options.DateMode switch
        {
            "Prefix" => formatted + options.DateSeparator + stem,
            "Suffix" => stem + options.DateSeparator + formatted,
            "Replace" => formatted,
            _ => throw new ArgumentException($"Unknown date mode: {options.DateMode}")
        };
    }

    private static string FormatCustomDate(DateTime date, string format)
    {
        var result = new StringBuilder();
        for (var index = 0; index < format.Length; index++)
        {
            if (format[index] != '%')
            {
                result.Append(format[index]);
                continue;
            }

            if (++index == format.Length)
            {
                throw new FormatException("A custom date format cannot end with %.");
            }

            var token = format[index];
            if (token == '%')
            {
                result.Append('%');
                continue;
            }

            var pattern = token switch
            {
                'a' => "ddd",
                'A' => "dddd",
                'b' or 'h' => "MMM",
                'B' => "MMMM",
                'd' => "dd",
                'm' => "MM",
                'y' => "yy",
                'Y' => "yyyy",
                'H' => "HH",
                'I' => "hh",
                'M' => "mm",
                'S' => "ss",
                'p' => "tt",
                'F' => "yyyy-MM-dd",
                'T' => "HH:mm:ss",
                'R' => "HH:mm",
                'j' => null,
                'w' => null,
                'u' => null,
                _ => throw new FormatException($"Unsupported custom date token: %{token}")
            };
            result.Append(token switch
            {
                'j' => date.DayOfYear.ToString("000", CultureInfo.InvariantCulture),
                'w' => ((int)date.DayOfWeek).ToString(CultureInfo.InvariantCulture),
                'u' => (date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek).ToString(CultureInfo.InvariantCulture),
                _ => date.ToString(pattern, CultureInfo.InvariantCulture)
            });
        }

        return result.ToString();
    }

    private static string AppendFolderName(string stem, RenameOptions options, string? directory)
    {
        if (options.FolderMode is not ("Prefix" or "Suffix") || string.IsNullOrEmpty(directory))
        {
            return stem;
        }

        var names = new List<string>();
        var current = directory;
        for (var level = 0; level < Math.Clamp(options.FolderLevels, 1, 20) && !string.IsNullOrEmpty(current); level++)
        {
            var name = Path.GetFileName(current.TrimEnd(Path.DirectorySeparatorChar));
            if (name.Length == 0)
            {
                break;
            }

            names.Insert(0, name);
            current = Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar));
        }

        if (names.Count == 0)
        {
            return stem;
        }

        var folders = string.Join(options.FolderSeparator, names);
        return options.FolderMode == "Prefix"
            ? folders + options.FolderSeparator + stem
            : stem + options.FolderSeparator + folders;
    }

    private static string RemoveParts(string stem, RenameOptions options)
    {
        if (options.RemoveFirst > 0)
        {
            stem = stem[Math.Min(options.RemoveFirst, stem.Length)..];
        }

        if (options.RemoveLast > 0)
        {
            stem = stem[..Math.Max(stem.Length - options.RemoveLast, 0)];
        }

        if (options.RemoveTo > 0)
        {
            var start = Math.Max(options.RemoveFrom, 1) - 1;
            if (start < stem.Length && options.RemoveTo > start)
            {
                var end = Math.Min(options.RemoveTo, stem.Length);
                stem = stem.Remove(start, end - start);
            }
        }

        if (!string.IsNullOrEmpty(options.CropText))
        {
            var index = stem.IndexOf(options.CropText, StringComparison.Ordinal);
            if (index >= 0)
            {
                stem = options.CropMode == "After"
                    ? stem[..(index + options.CropText.Length)]
                    : stem[index..];
            }
        }

        if (!string.IsNullOrEmpty(options.RemoveChars))
        {
            stem = string.Concat(stem.Where(c => !options.RemoveChars.Contains(c)));
        }

        if (options.TrimChars)
        {
            stem = string.Concat(stem.Where(c => !char.IsLetter(c)));
        }

        if (!string.IsNullOrWhiteSpace(options.RemoveWords))
        {
            foreach (var word in options.RemoveWords.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                stem = stem.Replace(word, string.Empty, StringComparison.Ordinal);
            }
        }

        if (options.RemoveAccents)
        {
            stem = string.Concat(stem.Normalize(System.Text.NormalizationForm.FormD)
                .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark))
                .Normalize(System.Text.NormalizationForm.FormC);
        }

        if (options.RemoveDigits)
        {
            stem = string.Concat(stem.Where(c => !char.IsDigit(c)));
        }

        if (options.RemoveHigh)
        {
            stem = string.Concat(stem.Where(c => c <= 127));
        }

        if (options.RemoveSymbols)
        {
            stem = string.Concat(stem.Where(c => !char.IsSymbol(c) && !char.IsPunctuation(c)));
        }

        if (options.RemoveDoubleSpaces)
        {
            stem = Regex.Replace(stem, " {2,}", " ");
        }

        if (options.TrimSpaces)
        {
            stem = stem.Trim();
        }

        stem = options.LeadDots switch
        {
            "One" => stem.StartsWith('.') ? stem[1..] : stem,
            "All" => stem.TrimStart('.'),
            _ => stem
        };

        return stem;
    }

    private static string ReplaceText(
        string input,
        string search,
        string replacement,
        StringComparison comparison,
        bool firstOnly)
    {
        if (search.Contains('*'))
        {
            var parts = search.Split('*');
            var pattern = string.Join(".*?", parts.Select(Regex.Escape));
            if (parts.Length > 1 && parts[^1].Length == 0)
            {
                pattern = string.Concat(pattern.AsSpan(0, pattern.Length - 3), ".*");
            }

            if (parts.Length > 1 && parts[0].Length == 0 && pattern.StartsWith(".*?", StringComparison.Ordinal))
            {
                pattern = string.Concat(".*", pattern.AsSpan(3));
            }

            var regexOptions = comparison == StringComparison.OrdinalIgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
            var regex = new Regex(pattern, regexOptions | RegexOptions.CultureInvariant);
            return firstOnly
                ? regex.Replace(input, _ => replacement, 1)
                : regex.Replace(input, _ => replacement);
        }

        if (firstOnly)
        {
            var index = input.IndexOf(search, comparison);
            return index < 0 ? input : string.Concat(input.AsSpan(0, index), replacement, input.AsSpan(index + search.Length));
        }

        return input.Replace(search, replacement, comparison);
    }

    private static string ToTitleCase(string value)
    {
        var characters = value.ToLowerInvariant().ToCharArray();
        var capitalizeNext = true;
        for (var index = 0; index < characters.Length; index++)
        {
            if (!char.IsLetterOrDigit(characters[index]))
            {
                capitalizeNext = true;
                continue;
            }

            if (capitalizeNext)
            {
                characters[index] = char.ToUpperInvariant(characters[index]);
                capitalizeNext = false;
            }
        }

        return new string(characters);
    }

    private static int ParseRadix(string numberType)
    {
        const string prefix = "Base ";
        if (numberType.StartsWith(prefix, StringComparison.Ordinal))
        {
            var rest = numberType.AsSpan(prefix.Length);
            var end = rest.IndexOf(' ');
            if (end >= 0)
            {
                rest = rest[..end];
            }

            if (int.TryParse(rest, out var radix) && radix is >= 2 and <= 36)
            {
                return radix;
            }
        }

        return 10;
    }

    private static string ToRadix(long value, int radix)
    {
        const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        if (value == 0)
        {
            return "0";
        }

        var negative = value < 0;
        var remaining = negative ? -(decimal)value : value;
        var buffer = new System.Text.StringBuilder();
        while (remaining > 0)
        {
            buffer.Insert(0, digits[(int)(remaining % radix)]);
            remaining = Math.Floor(remaining / radix);
        }

        return negative ? "-" + buffer : buffer.ToString();
    }

    private static string FormatNumber(long value, RenameOptions options)
    {
        var pad = Math.Clamp(options.NumberPadding, 0, 20);
        string formatted;

        switch (options.NumberType)
        {
            case "Roman":
                formatted = ToRoman(value);
                if (pad > 0)
                {
                    formatted = formatted.PadLeft(pad, '0');
                }
                break;

            case "AlphabetLower":
                formatted = ToAlphabet(value).ToLowerInvariant();
                if (pad > 0)
                {
                    formatted = formatted.PadLeft(pad, '0');
                }
                break;

            case "Hebrew":
                formatted = ToHebrew(value);
                if (pad > 0)
                {
                    formatted = formatted.PadLeft(pad, '0');
                }
                break;

            case "Alphabet":
                formatted = ToAlphabet(value);
                if (pad > 0)
                {
                    formatted = formatted.PadLeft(pad, '0');
                }
                break;

            default:
                var radix = ParseRadix(options.NumberType);
                formatted = radix == 10
                    ? (pad > 0 ? value.ToString($"D{pad}") : value.ToString())
                    : ToRadix(value, radix);
                if (radix != 10 && pad > 0)
                {
                    formatted = formatted.PadLeft(pad, '0');
                }
                break;
        }

        if (options.NumberCase == "Lower")
        {
            formatted = formatted.ToLowerInvariant();
        }
        else if (options.NumberCase == "Upper")
        {
            formatted = formatted.ToUpperInvariant();
        }

        return formatted;
    }

    private static string ToRoman(long number)
    {
        if (number <= 0 || number > 3999)
        {
            return number.ToString();
        }

        (int Value, string Numeral)[] map =
        [
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
            (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
            (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
        ];

        var sb = new StringBuilder();
        var remaining = (int)number;
        foreach (var (val, numeral) in map)
        {
            while (remaining >= val)
            {
                sb.Append(numeral);
                remaining -= val;
            }
        }

        return sb.ToString();
    }

    private static string ToHebrew(long number)
    {
        if (number <= 0)
        {
            return number.ToString();
        }

        string[] ones = ["", "א", "ב", "ג", "ד", "ה", "ו", "ז", "ח", "ט"];
        string[] tens = ["", "י", "כ", "ל", "מ", "נ", "ס", "ע", "פ", "צ"];
        string[] hundreds = ["", "ק", "ר", "ש", "ת"];

        var result = new System.Text.StringBuilder();
        var n = number;
        while (n >= 400)
        {
            result.Append('ת');
            n -= 400;
        }

        result.Append(hundreds[(int)(n / 100)]);
        n %= 100;
        if (n == 15)
        {
            result.Append("טו");
        }
        else if (n == 16)
        {
            result.Append("טז");
        }
        else
        {
            result.Append(tens[(int)(n / 10)]);
            result.Append(ones[(int)(n % 10)]);
        }

        return result.ToString();
    }

    private static string ToAlphabet(long number)
    {
        if (number <= 0)
        {
            return number.ToString();
        }

        var result = string.Empty;
        var n = number;
        while (n > 0)
        {
            n--;
            result = (char)('A' + (n % 26)) + result;
            n /= 26;
        }

        return result;
    }
}
