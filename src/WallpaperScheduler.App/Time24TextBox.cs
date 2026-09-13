using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WallpaperScheduler.App;

/// <summary>
/// Campo de horário 24h com máscara fixa HH:mm.
/// O ':' nunca é removido; digitação sobrescreve apenas os quatro dígitos.
/// </summary>
public sealed class Time24TextBox : TextBox
{
    private static readonly int[] DigitPositions = [0, 1, 3, 4];
    private bool _internalChange;

    public Time24TextBox()
    {
        MaxLength = 5;
        TextAlignment = TextAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;

        PreviewTextInput += OnPreviewTextInput;
        PreviewKeyDown += OnPreviewKeyDown;
        GotKeyboardFocus += OnGotKeyboardFocus;
        TextChanged += OnTextChanged;
        DataObject.AddPastingHandler(this, OnPaste);
    }

    private void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        EnsureMask();
        CaretIndex = NormalizeCaret(CaretIndex, forward: true);
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_internalChange || string.IsNullOrEmpty(Text)) return;
        if (IsValidTime(Text)) return;

        // Binding/migração pode fornecer um horário sem zero à esquerda.
        if (TimeOnly.TryParse(Text, out var parsed))
            SetText(parsed.ToString("HH:mm"), Math.Min(CaretIndex, 5));
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = true;
        EnsureMask();

        foreach (var digit in e.Text.Where(char.IsDigit))
        {
            var position = GetInputPosition();
            if (position < 0) break;
            if (!TrySetDigit(position, digit)) break;
            CaretIndex = NextDigitPosition(position);
            SelectionLength = 0;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Back)
        {
            e.Handled = true;
            EnsureMask();
            var position = PreviousDigitPosition(CaretIndex);
            ReplaceWithZero(position);
            CaretIndex = position;
            return;
        }

        if (e.Key is Key.Delete)
        {
            e.Handled = true;
            EnsureMask();
            var position = NormalizeCaret(CaretIndex, forward: true);
            ReplaceWithZero(position);
            CaretIndex = position;
            return;
        }

        if (e.Key is Key.Space)
            e.Handled = true;
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true))
        {
            e.CancelCommand();
            return;
        }

        var raw = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty;
        var digits = new string(raw.Where(char.IsDigit).Take(4).ToArray());
        if (digits.Length != 4)
        {
            e.CancelCommand();
            return;
        }

        var candidate = $"{digits[..2]}:{digits[2..]}";
        if (!IsValidTime(candidate))
        {
            e.CancelCommand();
            return;
        }

        SetText(candidate, 5);
        e.CancelCommand();
    }

    private void EnsureMask()
    {
        if (IsValidTime(Text)) return;

        if (TimeOnly.TryParse(Text, out var parsed))
            SetText(parsed.ToString("HH:mm"), Math.Min(CaretIndex, 5));
        else
            SetText("00:00", 0);
    }

    private int GetInputPosition()
    {
        if (SelectionLength > 0)
        {
            var firstSelectedDigit = DigitPositions.FirstOrDefault(p => p >= SelectionStart && p < SelectionStart + SelectionLength, -1);
            if (firstSelectedDigit >= 0)
                return firstSelectedDigit;
        }

        return NormalizeCaret(CaretIndex, forward: true);
    }

    private bool TrySetDigit(int position, char digit)
    {
        if (!DigitPositions.Contains(position)) return false;

        var chars = Text.ToCharArray();
        chars[position] = digit;

        // Validação imediata do relógio 24h: 00–23 e 00–59.
        if (position == 0 && digit > '2') return false;
        if (position == 1 && chars[0] == '2' && digit > '3') return false;
        if (position == 3 && digit > '5') return false;

        // Se a dezena da hora passar para 2 e a unidade antiga for inválida,
        // corrige somente a unidade sem remover a máscara.
        if (position == 0 && digit == '2' && chars[1] > '3')
            chars[1] = '0';

        var candidate = new string(chars);
        if (!IsValidTime(candidate)) return false;

        SetText(candidate, position);
        return true;
    }

    private void ReplaceWithZero(int position)
    {
        if (!DigitPositions.Contains(position)) return;
        var chars = Text.ToCharArray();
        chars[position] = '0';
        SetText(new string(chars), position);
    }

    private void SetText(string value, int caret)
    {
        _internalChange = true;
        try
        {
            Text = value;
            CaretIndex = Math.Clamp(caret, 0, value.Length);
        }
        finally
        {
            _internalChange = false;
        }
    }

    private static bool IsValidTime(string? value)
    {
        if (value is null || value.Length != 5 || value[2] != ':') return false;
        return TimeOnly.TryParseExact(value, "HH:mm", out _);
    }

    private static int NormalizeCaret(int caret, bool forward)
    {
        caret = Math.Clamp(caret, 0, 5);
        if (caret == 2) return forward ? 3 : 1;
        if (caret >= 5) return 4;
        return DigitPositions.Contains(caret) ? caret : (forward ? 3 : 1);
    }

    private static int NextDigitPosition(int current) => current switch
    {
        0 => 1,
        1 => 3,
        3 => 4,
        _ => 4
    };

    private static int PreviousDigitPosition(int caret) => caret switch
    {
        <= 0 => 0,
        1 => 0,
        2 => 1,
        3 => 1,
        4 => 3,
        _ => 4
    };
}
