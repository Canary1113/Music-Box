using Microsoft.UI.Xaml.Controls;

namespace 音乐魔盒
{
    public sealed partial class ShellContentHost : UserControl
    {
        public ShellContentHost()
        {
            InitializeComponent();
        }

        public Frame MainFrame => ContentFrame;
    }
}
