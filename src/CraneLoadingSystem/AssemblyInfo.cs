using System.Windows;

// WPF 主题信息程序集级特性：指定主题资源字典与泛型资源字典的查找位置
[assembly: ThemeInfo(
    // 主题专用资源字典位置：None=不使用外部主题资源（资源未找到时回退到 page / app 资源）
    ResourceDictionaryLocation.None,
    // 泛型资源字典位置：SourceAssembly=从本程序集查找 generic.xaml（最终回退）
    ResourceDictionaryLocation.SourceAssembly
)]
