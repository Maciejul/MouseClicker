using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using ComboBox = System.Windows.Controls.ComboBox;
using Control = System.Windows.Controls.Control;
using CheckBox = System.Windows.Controls.CheckBox;
using RadioButton = System.Windows.Controls.RadioButton;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Size = System.Windows.Size;
using Xunit;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace MouseClicker.Tests;

public class ThemeTests
{
    /// <summary>Runs on an STA thread with the real App resources loaded (WPF needs both), and rethrows what went wrong there.</summary>
    private static void OnUiThread(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App();
                app.InitializeComponent();
                body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    [Fact]
    public void App_resources_load_and_switching_the_palette_replaces_the_brushes()
    {
        OnUiThread(() =>
        {
            var box = new ComboBox();
            box.Items.Add(new ComboBoxItem { Content = "Left button" });
            box.SelectedIndex = 0;
            var controls = new Control[] { new CheckBox { Content = "x" }, new RadioButton { Content = "y" }, box, new Button { Content = "z" }, new TextBox(), new Separator(), new ToggleButton() };
            var panel = new StackPanel();
            foreach (var c in controls) panel.Children.Add(c);

            panel.Measure(new Size(400, 400));
            panel.Arrange(new Rect(0, 0, 400, 400));

            // A template that fails to load throws above; one that loads has visual children.
            foreach (var c in controls) Assert.True(System.Windows.Media.VisualTreeHelper.GetChildrenCount(c) > 0, c.GetType().Name);

            ThemeService.Apply(dark: false);
            var light = ((SolidColorBrush)Application.Current.Resources["WindowBg"]).Color;
            ThemeService.Apply(dark: true);
            var dark = ((SolidColorBrush)Application.Current.Resources["WindowBg"]).Color;

            Assert.NotEqual(light, dark);
            Assert.True(ThemeService.Dark);
            // Dark layers by lightness: window, then card, then field.
            static int Sum(Color c) => c.R + c.G + c.B;
            var res = Application.Current.Resources;
            Assert.True(Sum(((SolidColorBrush)res["WindowBg"]).Color) < Sum(((SolidColorBrush)res["CardBg"]).Color));
            Assert.True(Sum(((SolidColorBrush)res["CardBg"]).Color) < Sum(((SolidColorBrush)res["FieldBrush"]).Color));
        });
    }
}
