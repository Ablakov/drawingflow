namespace PanelTracker.Core.Scanning;

/// <summary>«Естественная» сортировка идентификаторов: N2 раньше N10, буквы без учёта регистра.</summary>
public sealed class PanelIdComparer : IComparer<string>
{
    public static PanelIdComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                var startI = i;
                var startJ = j;
                while (i < x.Length && char.IsDigit(x[i]))
                {
                    i++;
                }

                while (j < y.Length && char.IsDigit(y[j]))
                {
                    j++;
                }

                var numX = x.AsSpan(startI, i - startI).TrimStart('0');
                var numY = y.AsSpan(startJ, j - startJ).TrimStart('0');
                if (numX.Length != numY.Length)
                {
                    return numX.Length.CompareTo(numY.Length);
                }

                var cmp = numX.CompareTo(numY, StringComparison.Ordinal);
                if (cmp != 0)
                {
                    return cmp;
                }
            }
            else
            {
                var cmp = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                if (cmp != 0)
                {
                    return cmp;
                }

                i++;
                j++;
            }
        }

        return (x.Length - i).CompareTo(y.Length - j);
    }
}
