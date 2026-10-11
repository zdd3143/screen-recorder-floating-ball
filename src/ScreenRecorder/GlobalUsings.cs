// 同时启用 WPF 与 WinForms（托盘图标用 WinForms），这里统一消除类型歧义。
global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
global using MessageBoxButton = System.Windows.MessageBoxButton;
global using MessageBoxImage = System.Windows.MessageBoxImage;
global using MessageBoxResult = System.Windows.MessageBoxResult;
global using Clipboard = System.Windows.Clipboard;
global using Point = System.Windows.Point;
global using KeyEventArgs = System.Windows.Input.KeyEventArgs;
global using MouseEventArgs = System.Windows.Input.MouseEventArgs;
global using MenuItem = System.Windows.Controls.MenuItem;
global using Color = System.Windows.Media.Color;
global using Brushes = System.Windows.Media.Brushes;
global using ContextMenu = System.Windows.Controls.ContextMenu;
