using System.Windows;
using System.Windows.Input;

namespace AnalizaKoloruZdjęcia.Helpers
{
    public static class DragDropHelper
    {
        public static readonly DependencyProperty DropCommandProperty =
            DependencyProperty.RegisterAttached(
                "DropCommand",
                typeof(ICommand),
                typeof(DragDropHelper),
                new PropertyMetadata(null, OnDropCommandChanged));

        public static ICommand GetDropCommand(DependencyObject obj)
        {
            return (ICommand)obj.GetValue(DropCommandProperty);
        }

        public static void SetDropCommand(DependencyObject obj, ICommand value)
        {
            obj.SetValue(DropCommandProperty, value);
        }

        private static void OnDropCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is UIElement element)
            {
                element.AllowDrop = true;
                element.Drop -= Element_Drop;
                element.PreviewDragOver -= Element_PreviewDragOver;

                if (e.NewValue != null)
                {
                    element.Drop += Element_Drop;
                    element.PreviewDragOver += Element_PreviewDragOver;
                }
            }
        }

        private static void Element_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private static void Element_Drop(object sender, DragEventArgs e)
        {
            if (sender is UIElement element)
            {
                ICommand command = GetDropCommand(element);
                if (command != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (command.CanExecute(files))
                    {
                        command.Execute(files);
                    }
                }
            }
        }
    }
}
