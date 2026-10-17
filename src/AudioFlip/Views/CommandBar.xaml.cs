namespace AudioFlip;

using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

public sealed partial class CommandBar : UserControl
{
    public static readonly DependencyProperty CommandTextProperty = DependencyProperty.Register(nameof(CommandText), typeof(string), typeof(CommandBar), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty CopyCommandProperty = DependencyProperty.Register(nameof(CopyCommand), typeof(ICommand), typeof(CommandBar), null);
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(CommandBar), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty LabelCommandProperty = DependencyProperty.Register(nameof(LabelCommand), typeof(ICommand), typeof(CommandBar), null);

    public CommandBar() => InitializeComponent();

    public string CommandText
    {
        get => (string)GetValue(CommandTextProperty);
        set => SetValue(CommandTextProperty, value);
    }

    public ICommand CopyCommand
    {
        get => (ICommand)GetValue(CopyCommandProperty);
        set => SetValue(CopyCommandProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public ICommand LabelCommand
    {
        get => (ICommand)GetValue(LabelCommandProperty);
        set => SetValue(LabelCommandProperty, value);
    }

    public Visibility GetLabelVisibility(string label) => string.IsNullOrEmpty(label) ? Visibility.Collapsed : Visibility.Visible;
}
