using Microsoft.Extensions.DependencyInjection;

namespace ShapeEditor.Maui;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
        UserAppTheme = AppTheme.Unspecified;
    }

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}