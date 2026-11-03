namespace ShapeEditor.Maui;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		// 分析页不在标签栏里，按需压入当前标签页的导航栈
		Routing.RegisterRoute(
			AppNavigation.AnalysisRoute,
			typeof(AnalysisPage));
	}
}
