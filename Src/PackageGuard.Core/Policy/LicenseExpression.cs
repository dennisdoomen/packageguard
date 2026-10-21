using System.Text.RegularExpressions;

namespace PackageGuard.Core.Policy;

/// <summary>
/// A parsed SPDX license expression, such as <c>MIT OR Apache-2.0</c> or
/// <c>GPL-2.0-only WITH Classpath-exception-2.0</c>. A license that is not a valid expression, such as free text,
/// becomes a single <see cref="Term"/> that holds the text as is.
/// </summary>
internal abstract class LicenseExpression
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

    /// <summary>
    /// Parses <paramref name="license"/>. Operators are not case-sensitive.
    /// </summary>
    public static LicenseExpression Parse(string license)
    {
        var parser = new Parser(Tokenizer.Matches(license).Select(match => match.Value).ToArray());
        LicenseExpression? expression = parser.ParseOr();

        return expression is not null && parser.IsAtEnd ? expression : new Term(license.Trim(), null);
    }

    /// <summary>
    /// Finds the entries in <paramref name="entries"/> that allow <paramref name="license"/>. An <c>OR</c> needs one
    /// allowed operand, since the consumer can choose. An <c>AND</c> needs every operand to be allowed. A <c>WITH</c>
    /// term is allowed by an entry for the full term or for the license without the exception.
    /// </summary>
    /// <returns>The entries that together allow the license, or an empty list if the license is not allowed.</returns>
    public static IReadOnlyList<string> FindAllowingEntries(string? license, ICollection<string> entries)
    {
        if (string.IsNullOrWhiteSpace(license) || entries.Count == 0)
        {
            return [];
        }

        LicenseExpression expression = Parse(license);

        return AsMatch(FindEntry(entries, expression.ToString())) ?? expression.Evaluate(
            term => AsMatch(FindEntry(entries, term.ToString()) ?? FindEntry(entries, term.Id) ?? FindEntry(entries, term.BaseId)),
            (left, right) => left ?? right,
            Combine) ?? [];
    }

    /// <summary>
    /// Finds the entries in <paramref name="entries"/> that restrict <paramref name="license"/>, as a deny list or a
    /// warn list does. An <c>OR</c> is only restricted if every operand is, since one acceptable option is enough. An
    /// <c>AND</c> is restricted if any operand is. A <c>WITH</c> term is only restricted by an entry for the full
    /// term, since the exception is what makes it acceptable.
    /// </summary>
    /// <returns>The entries that together restrict the license, or an empty list if the license is not restricted.</returns>
    public static IReadOnlyList<string> FindRestrictingEntries(string? license, ICollection<string> entries)
    {
        if (string.IsNullOrWhiteSpace(license) || entries.Count == 0)
        {
            return [];
        }

        LicenseExpression expression = Parse(license);

        return AsMatch(FindEntry(entries, expression.ToString())) ?? expression.Evaluate(
            term => AsMatch(FindEntry(entries, term.ToString())),
            Combine,
            (left, right) => left ?? right) ?? [];
    }

    /// <summary>
    /// Describes that <paramref name="entries"/> in the named list matched <paramref name="license"/>, and names the
    /// full license when the entries only matched part of it.
    /// </summary>
    public static string DescribeMatch(string listName, IReadOnlyList<string> entries, string? license)
    {
        string match = entries.Count == 1
            ? $"matches {listName} license entry {JoinQuoted(entries, "and")}"
            : $"matches {listName} license entries {JoinQuoted(entries, "and")}";

        return entries.Count == 1 && entries[0].Equals(license, StringComparison.OrdinalIgnoreCase)
            ? match
            : $"{match} (from \"{license}\")";
    }

    /// <summary>
    /// Quotes each of <paramref name="entries"/> and joins them with <paramref name="conjunction"/>, such as
    /// <c>"MIT" and "Apache-2.0"</c>.
    /// </summary>
    public static string JoinQuoted(IEnumerable<string> entries, string conjunction) =>
        string.Join($" {conjunction} ", entries.Select(entry => $"\"{entry}\""));

    /// <summary>
    /// Folds this expression into a single value, using <paramref name="term"/> for every single license and
    /// <paramref name="or"/> and <paramref name="and"/> to combine the operands of compound expressions.
    /// </summary>
    public abstract T Evaluate<T>(Func<Term, T> term, Func<T, T, T> or, Func<T, T, T> and);

    /// <summary>
    /// Returns the normalized form of this expression, which is used to compare it to configured entries. The operands
    /// of <c>OR</c> and <c>AND</c> are sorted, since their order has no meaning.
    /// </summary>
    public abstract override string ToString();

    private static string[]? AsMatch(string? entry) => entry is null ? null : [entry];

    private static string[]? Combine(string[]? left, string[]? right) =>
        left is not null && right is not null ? [..left.Union(right, StringComparer.OrdinalIgnoreCase)] : null;

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
    /// A single license, such as <c>MIT</c>, optionally with an exception, such as
    /// <c>GPL-2.0-only WITH Classpath-exception-2.0</c>.
    /// </summary>
    public sealed class Term(string id, string? exceptionId) : LicenseExpression
    {
        /// <summary>
        /// Gets the license identifier, with deprecated identifiers replaced by their current form.
        /// </summary>
        public string Id { get; } = Normalize(id);

        /// <summary>
        /// Gets the version this "or later" term starts from, such as <c>GPL-2.0-only</c> for <c>GPL-2.0-or-later</c>,
        /// or <see langword="null"/> if the term is not an "or later" license.
        /// </summary>
        public string? BaseId =>
            Id.EndsWith('+') ? Id[..^1] :
            Id.EndsWith("-or-later", StringComparison.OrdinalIgnoreCase) ? Id[..^"-or-later".Length] + "-only" :
            null;

        /// <summary>
        /// Gets the exception of a <c>WITH</c> term, such as <c>Classpath-exception-2.0</c>.
        /// </summary>
        private string? ExceptionId { get; } = exceptionId;

        /// <inheritdoc/>
        public override T Evaluate<T>(Func<Term, T> term, Func<T, T, T> or, Func<T, T, T> and) => term(this);

        /// <inheritdoc/>
        public override string ToString() => ExceptionId is null ? Id : $"{Id} {With} {ExceptionId}";
    }

    /// <summary>
    /// Two expressions combined with <c>OR</c> or <c>AND</c>.
    /// </summary>
    private sealed class Compound(string @operator, LicenseExpression left, LicenseExpression right) : LicenseExpression
    {
        private string Operator { get; } = @operator;

        public override T Evaluate<T>(Func<Term, T> term, Func<T, T, T> or, Func<T, T, T> and)
        {
            T leftValue = left.Evaluate(term, or, and);
            T rightValue = right.Evaluate(term, or, and);

            return Operator == Or ? or(leftValue, rightValue) : and(leftValue, rightValue);
        }

        public override string ToString() =>
            string.Join($" {Operator} ", GetOperands().Select(Wrap).Order(StringComparer.OrdinalIgnoreCase));

        /// <summary>
        /// Returns the operands of this expression, including those of nested expressions with the same operator, so
        /// <c>A OR (B OR C)</c> and <c>(C OR B) OR A</c> have the same normalized form.
        /// </summary>
        private IEnumerable<LicenseExpression> GetOperands() => Flatten(left).Concat(Flatten(right));

        private IEnumerable<LicenseExpression> Flatten(LicenseExpression operand) =>
            operand is Compound compound && compound.Operator == Operator ? compound.GetOperands() : [operand];

        private static string Wrap(LicenseExpression operand) =>
            operand is Compound ? $"({operand})" : operand.ToString();
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
                left = right is null ? null : new Compound(@operator, left, right);
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
                return id is null ? null : new Term(id, null);
            }

            string? exceptionId = NextIdentifier();
            return exceptionId is null ? null : new Term(id, exceptionId);
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
