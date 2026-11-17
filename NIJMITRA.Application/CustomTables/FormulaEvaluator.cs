using System.Globalization;
using System.Text.RegularExpressions;

namespace NIJMITRA.Application.CustomTables;

/// <summary>
/// Safe arithmetic evaluator for formula columns: numbers, + - * / %, parentheses, and {Column Name} references.
/// No code execution, no functions — just a tiny recursive-descent parser.
/// </summary>
public static partial class FormulaEvaluator
{
    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex Reference();

    public static IReadOnlyList<string> References(string formula) => Reference().Matches(formula).Select(m => m.Groups[1].Value.Trim()).Distinct().ToList();

    public static decimal? Evaluate(string formula, Func<string, decimal?> resolve)
    {
        var missing = false;
        var expr = Reference().Replace(formula, m =>
        {
            var v = resolve(m.Groups[1].Value.Trim());
            if (v == null) { missing = true; return "0"; }
            return v.Value.ToString(CultureInfo.InvariantCulture);
        });
        if (missing) return null;
        try
        {
            var p = new Parser(expr);
            var result = p.ParseExpression();
            p.SkipWs();
            if (!p.End) return null;
            return Math.Round(result, 4);
        }
        catch (Exception ex) when (ex is DivideByZeroException or FormatException or OverflowException or InvalidOperationException) { return null; }
    }

    public static bool IsValid(string formula, IEnumerable<string> numericColumns)
    {
        var cols = numericColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (References(formula).Any(r => !cols.Contains(r))) return false;
        return Evaluate(formula, _ => 1m) != null;
    }

    private sealed class Parser(string s)
    {
        private int _i;
        public bool End => _i >= s.Length;
        public void SkipWs() { while (_i < s.Length && char.IsWhiteSpace(s[_i])) _i++; }

        public decimal ParseExpression()
        {
            var v = ParseTerm();
            while (true)
            {
                SkipWs();
                if (End) return v;
                if (s[_i] == '+') { _i++; v += ParseTerm(); }
                else if (s[_i] == '-') { _i++; v -= ParseTerm(); }
                else return v;
            }
        }

        private decimal ParseTerm()
        {
            var v = ParseFactor();
            while (true)
            {
                SkipWs();
                if (End) return v;
                if (s[_i] == '*') { _i++; v *= ParseFactor(); }
                else if (s[_i] == '/') { _i++; v /= ParseFactor(); }
                else if (s[_i] == '%') { _i++; v %= ParseFactor(); }
                else return v;
            }
        }

        private decimal ParseFactor()
        {
            SkipWs();
            if (End) throw new FormatException();
            if (s[_i] == '-') { _i++; return -ParseFactor(); }
            if (s[_i] == '+') { _i++; return ParseFactor(); }
            if (s[_i] == '(')
            {
                _i++;
                var v = ParseExpression();
                SkipWs();
                if (End || s[_i] != ')') throw new FormatException();
                _i++;
                return v;
            }
            var start = _i;
            while (_i < s.Length && (char.IsDigit(s[_i]) || s[_i] == '.')) _i++;
            if (start == _i) throw new FormatException();
            return decimal.Parse(s[start.._i], CultureInfo.InvariantCulture);
        }
    }
}
