using System.Windows;
using System.Windows.Input;
using WpfDataFormats = System.Windows.DataFormats;
using WpfDataObject = System.Windows.DataObject;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace WallpaperScheduler.App;

/// <summary>
/// Restringe um TextBox comum a dígitos, preservando o estilo visual padrão da aplicação.
/// </summary>
public static class NumericTextBoxBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(NumericTextBoxBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not WpfTextBox textBox) return;

        textBox.PreviewTextInput -= OnPreviewTextInput;
        textBox.PreviewKeyDown -= OnPreviewKeyDown;
        WpfDataObject.RemovePastingHandler(textBox, OnPaste);

        if (e.NewValue is not true) return;

        // O template global usa o Padding como margem do PART_ContentHost. Em um campo
        // de 36 px, o padding vertical padrão reduz demais a viewport e o ScrollViewer
        // pode recortar a parte superior dos algarismos durante a edição.
        textBox.Padding = new Thickness(textBox.Padding.Left, 0, textBox.Padding.Right, 0);
        textBox.VerticalContentAlignment = VerticalAlignment.Center;

        textBox.PreviewTextInput += OnPreviewTextInput;
        textBox.PreviewKeyDown += OnPreviewKeyDown;
        WpfDataObject.AddPastingHandler(textBox, OnPaste);
    }

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = string.IsNullOrEmpty(e.Text) || e.Text.Any(character => !char.IsDigit(character));

    private static void OnPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Space)
            e.Handled = true;
    }

    private static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.SourceDataObject.GetDataPresent(WpfDataFormats.UnicodeText, true))
        {
            e.CancelCommand();
            return;
        }

        var value = e.SourceDataObject.GetData(WpfDataFormats.UnicodeText) as string ?? string.Empty;
        if (value.Length == 0 || value.Any(character => !char.IsDigit(character)))
            e.CancelCommand();
    }
}