using System.Windows.Controls;

namespace ProfileOnPC.Helpers;

/// <summary>Чтение и установка значения ComboBox по строковому Tag (см. разметку окон).</summary>
public static class ComboBoxHelper
{
    public static string GetTag(ComboBox comboBox, string fallback) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

    public static void SelectByTag(ComboBox comboBox, string tag)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }
}
