using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

// Общая тёмная тема для окон "Словарь" и "Модели" — в стиле попапа перевода (TranslationPopup).
static class AppTheme
{
    // Каждое окно ("Словарь", "Модели", попап, оверлей) работает на своём отдельном STA-потоке
    // со своим Dispatcher'ом. Freezable-объекты (кисти, геометрия) без Freeze() привязаны
    // к потоку, который первым их коснулся, — общие статические кисти без заморозки роняют
    // окно на втором потоке с "объект принадлежит другому потоку". Freeze() делает их
    // неизменяемыми и безопасными для использования с любого потока.
    public static readonly Brush WindowBackground = Frozen(new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E)));
    public static readonly Brush ButtonBackground = Frozen(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x3C)));
    public static readonly Brush ButtonHoverBackground = Frozen(new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x4E)));
    public static readonly Brush AccentBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA)));
    public static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)));
    public static readonly Brush HeadingBrush = Brushes.White;
    public static readonly Brush DividerBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)));

    private static Brush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    public static void ApplyWindow(Window window)
    {
        window.Background = WindowBackground;
        window.Foreground = TextBrush;
        window.FontFamily = new FontFamily("Segoe UI");
        window.FontSize = 13;
    }

    public static Border CreateDivider(Thickness? margin = null) => new()
    {
        Height = 1,
        Background = DividerBrush,
        Margin = margin ?? new Thickness(0, 8, 0, 8),
    };

    public static Style CreateButtonStyle()
    {
        var border = new FrameworkElementFactory(typeof(Border), "border");
        border.SetValue(Border.BackgroundProperty, ButtonBackground);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.PaddingProperty, new Thickness(12, 6, 12, 6));

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, ButtonHoverBackground, "border"));
        template.Triggers.Add(hover);

        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.ForegroundProperty, HeadingBrush));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        return style;
    }

    public static Style CreateCheckBoxStyle()
    {
        var root = new FrameworkElementFactory(typeof(StackPanel));
        root.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        root.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);

        var box = new FrameworkElementFactory(typeof(Border), "box");
        box.SetValue(FrameworkElement.WidthProperty, 18.0);
        box.SetValue(FrameworkElement.HeightProperty, 18.0);
        box.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        box.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
        box.SetValue(Border.BorderBrushProperty, DividerBrush);
        box.SetValue(Border.BackgroundProperty, ButtonBackground);
        box.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 10, 0));
        box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);

        var check = new FrameworkElementFactory(typeof(Path), "check");
        check.SetValue(Path.DataProperty, Geometry.Parse("M 3,9 L 7,13 L 15,4"));
        check.SetValue(Shape.StrokeProperty, HeadingBrush);
        check.SetValue(Shape.StrokeThicknessProperty, 2.0);
        check.SetValue(Shape.StrokeStartLineCapProperty, PenLineCap.Round);
        check.SetValue(Shape.StrokeEndLineCapProperty, PenLineCap.Round);
        check.SetValue(Shape.StrokeLineJoinProperty, PenLineJoin.Round);
        check.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        box.AppendChild(check);
        root.AppendChild(box);

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
        root.AppendChild(content);

        var template = new ControlTemplate(typeof(CheckBox)) { VisualTree = root };

        var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, AccentBrush, "box"));
        checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, AccentBrush, "box"));
        checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "check"));
        template.Triggers.Add(checkedTrigger);

        var style = new Style(typeof(CheckBox));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        return style;
    }

    public static Style CreateListBoxItemStyle()
    {
        var border = new FrameworkElementFactory(typeof(Border), "itemBorder");
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.PaddingProperty, new Thickness(10, 8, 10, 8));
        border.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 4));

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        border.AppendChild(content);

        var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = border };

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, ButtonBackground, "itemBorder"));
        template.Triggers.Add(hover);

        var selected = new Trigger { Property = Selector.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, ButtonHoverBackground, "itemBorder"));
        selected.Setters.Add(new Setter(Border.BorderBrushProperty, AccentBrush, "itemBorder"));
        template.Triggers.Add(selected);

        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        return style;
    }
}
