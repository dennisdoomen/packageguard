using System.Text.RegularExpressions;

namespace PackageGuard.Core.Policy;

/// <summary>
/// A parsed SPDX license expression, such as <c>MIT OR Apache-2.0</c> or
/// <c>GPL-2.0-only WITH Classpath-exception-2.0</c>. A license that is not a valid expression, such as free text,
/// becomes a single term that holds the text as is.
/// </summary>
internal sealed class LicenseExpression
{
    private const string Or = "OR";
    private const string And = "AND";
    private const string With = "WITH";

    private static readonly Regex Tokenizer = new(@"\(|\)|[^\s()]+", RegexOptions.Compiled);

    /// <summary>
    /// Deprecated GNU identifiers that SPDX split into an <c>-only</c> and an <c>-or-later</c> variant.
    /// </summary>
    private static readonly string[] DeprecatedGnuIds =
    [
        "GPL-1.0", "GPL-2.0", "GPL-3.0", "LGPL-2.0", "LGPL-2.1", "LGPL-3.0", "AGPL-1.0", "AGPL-3.0",
        "GFDL-1.1", "GFDL-1.2", "GFDL-1.3"
    ];

    private LicenseExpression(string? @operator, LicenseExpression? left, LicenseExpression? right)
    {
        Operator = @operator;
        Left = left;
        Right = right;
    }

    private LicenseExpression(string id, string? exception)
    {
        Id = Normalize(id);
        Exception = exception;
    }

    /// <summary>
    /// Gets <c>OR</c> or <c>AND</c> for a compound expression, or <see langword="null"/> for a single term.
    /// </summary>
    private string? Operator { get; }

    private LicenseExpression? Left { get; }

    private LicenseExpression? Right { get; }

    /// <summary>
    /// Gets the license identifier of a single term, with deprecated identifiers replaced by their current form.
    /// </summary>
    public string Id { get; } = "";

    /// <summary>
    /// Gets the exception of a <c>WITH</c> term, such as <c>Classpath-exception-2.0</c>.
    /// </summary>
    public string? Exception { get; }

    /// <summary>
    /// Gets the version this "or later" term starts from, such as <c>GPL-2.0-only</c> for <c>GPL-2.0-or-later</c>,
    /// or <see langword="null"/> if the term is not an "or later" license.
    /// </summary>
    private string? BaseId =>
        Id.EndsWith('+') ? Id[..^1] :
        Id.EndsWith("-or-later", StringComparison.OrdinalIgnoreCase) ? Id[..^"-or-later".Length] + "-only" :
        null;

    /// <summary>
    /// Parses <paramref name="license"/>. Operators are not case-sensitive.
    /// </summary>
    public static LicenseExpression Parse(string license)
    {
        var parser = new Parser(Tokenizer.Matches(license).Select(match => match.Value).ToArray());
        LicenseExpression? expression = parser.ParseOr();

        return expression is not null && parser.IsAtEnd ? expression : new LicenseExpression(license.Trim(), null);
    }

    /// <summary>
    /// Finds the entry in <paramref name="entries"/> that allows <paramref name="license"/>. An <c>OR</c> needs one
    /// allowed operand, since the consumer can choose. An <c>AND</c> needs every operand to be allowed. A <c>WITH</c>
    /// term is allowed by an entry for the full term or for the license without the exception.
    /// </summary>
    /// <returns>The matching entry, or <see langword="null"/> if the license is not allowed.</returns>
    public static string? FindAllowingEntry(string? license, ICollection<string> entries)
    {
        if (string.IsNullOrWhiteSpace(license) || entries.Count == 0)
        {
            return null;
        }

        LicenseExpression expression = Parse(license);

        return FindEntry(entries, expression.ToString()) ?? expression.Evaluate(
            term => FindEntry(entries, term.ToString()) ?? FindEntry(entries, term.Id) ?? FindEntry(entries, term.BaseId),
            (left, right) => left ?? right,
            (left, right) => left is not null && right is not null ? left : null);
    }

    /// <summary>
    /// Finds the entry in <paramref name="entries"/> that denies <paramref name="license"/>. An <c>OR</c> is only
    /// denied if every operand is, since one acceptable option is enough. An <c>AND</c> is denied if any operand is.
    /// A <c>WITH</c> term is only denied by an entry for the full term, since the exception is what makes it
    /// acceptable.
    /// </summary>
    /// <returns>The matching entry, or <see langword="null"/> if the license is not denied.</returns>
    public static string? FindDenyingEntry(string? license, ICollection<string> entries)
    {
        if (string.IsNullOrWhiteSpace(license) || entries.Count == 0)
        {
            return null;
        }

        LicenseExpression expression = Parse(license);

        return FindEntry(entries, expression.ToString()) ?? expression.Evaluate(
            term => FindEntry(entries, term.ToString()),
            (left, right) => left is not null && right is not null ? left : null,
            (left, right) => left ?? right);
    }

    /// <summary>
    /// Describes that <paramref name="entry"/> in the named list matched <paramref name="license"/>, and names the
    /// full license when the entry only matched part of it.
    /// </summary>
    public static string DescribeMatch(string listName, string entry, string? license) =>
        entry.Equals(license, StringComparison.OrdinalIgnoreCase)
            ? $"matches {listName} license entry \"{entry}\""
            : $"matches {listName} license entry \"{entry}\" (from \"{license}\")";

    /// <summary>
    /// Folds this expression into a single value, using <paramref name="term"/> for every single license and
    /// <paramref name="or"/> and <paramref name="and"/> to combine the operands of compound expressions.
    /// </summary>
    public T Evaluate<T>(Func<LicenseExpression, T> term, Func<T, T, T> or, Func<T, T, T> and) => Operator switch
    {
        null => term(this),
        Or => or(Left!.Evaluate(term, or, and), Right!.Evaluate(term, or, and)),
        _ => and(Left!.Evaluate(term, or, and), Right!.Evaluate(term, or, and))
    };

    /// <summary>
    /// Returns the normalized form of this expression, which is used to compare it to configured entries.
    /// </summary>
    public override string ToString()
    {
        if (Operator is null)
        {
            return Exception is null ? Id : $"{Id} {With} {Exception}";
        }

        return $"{Wrap(Left!)} {Operator} {Wrap(Right!)}";
    }

    private string Wrap(LicenseExpression operand) =>
        operand.Operator is not null && operand.Operator != Operator ? $"({operand})" : operand.ToString();

    // ponytail: parses every entry on each lookup; cache the parsed entries if big policies make this slow
    private static string? FindEntry(IEnumerable<string> entries, string? normalizedLicense) =>
        normalizedLicense is null
            ? null
            : entries.FirstOrDefault(entry =>
                Parse(entry).ToString().Equals(normalizedLicense, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string id)
    {
        bool isOrLater = id.EndsWith('+');
        string baseId = isOrLater ? id[..^1] : id;

        string? deprecatedId = DeprecatedGnuIds.FirstOrDefault(gnuId => gnuId.Equals(baseId, StringComparison.OrdinalIgnoreCase));
        if (deprecatedId is null)
        {
            return id;
        }

        return deprecatedId + (isOrLater ? "-or-later" : "-only");
    }

    /// <summary>
    /// A recursive descent parser for <c>or := and (OR and)*</c>, <c>and := term (AND term)*</c> and
    /// <c>term := "(" or ")" | id [WITH id]</c>. Each method returns <see langword="null"/> when the tokens do not
    /// form a valid expression.
    /// </summary>
    private sealed class Parser(string[] tokens)
    {
        private int position;

        public bool IsAtEnd => position == tokens.Length;

        public LicenseExpression? ParseOr() => ParseBinary(Or, ParseAnd);

        private LicenseExpression? ParseAnd() => ParseBinary(And, ParseTerm);

        private LicenseExpression? ParseBinary(string @operator, Func<LicenseExpression?> parseOperand)
        {
            LicenseExpression? left = parseOperand();
            while (left is not null && Accept(@operator))
            {
                LicenseExpression? right = parseOperand();
                left = right is null ? null : new LicenseExpression(@operator, left, right);
            }

            return left;
        }

        private LicenseExpression? ParseTerm()
        {
            if (Accept("("))
            {
                LicenseExpression? inner = ParseOr();
                return Accept(")") ? inner : null;
            }

            string? id = NextIdentifier();
            if (id is null || !Accept(With))
            {
                return id is null ? null : new LicenseExpression(id, null);
            }

            string? exception = NextIdentifier();
            return exception is null ? null : new LicenseExpression(id, exception);
        }

        private string? NextIdentifier()
        {
            if (IsAtEnd || tokens[position] is "(" or ")" || IsOperator(tokens[position]))
            {
                return null;
            }

            return tokens[position++];
        }

        private bool Accept(string token)
        {
            if (IsAtEnd || !tokens[position].Equals(token, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            position++;
            return true;
        }

        private static bool IsOperator(string token) =>
            token.Equals(Or, StringComparison.OrdinalIgnoreCase) ||
            token.Equals(And, StringComparison.OrdinalIgnoreCase) ||
            token.Equals(With, StringComparison.OrdinalIgnoreCase);
    }
}
