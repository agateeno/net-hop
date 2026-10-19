using Windows.Management.Deployment;

namespace NetHop.App.Services;

/// <summary>
/// "Apply with NetHop" in the Windows 11 context menu (not only under "Show more options").
/// Windows shows first-level entries only for handlers declared by a package, so the installer ships
/// NetHop.ContextMenu.msix, a sparse package whose content is the install folder (NetHop.ShellExtension.dll).
/// Packages are registered per user: NetHop registers it at startup, and again after an upgrade.
/// </summary>
public static class ContextMenuPackage
{
    private const string PackageName = "NetHop.ContextMenu";

    private static string PackagePath => Path.Combine(AppContext.BaseDirectory, PackageName + ".msix");

    /// <summary>Registers or updates the package for the current user. Never throws; failures go to the log.</summary>
    public static Task EnsureRegisteredAsync() => Task.Run(async () =>
    {
        // The new context menu is Windows 11 only; development builds have no package.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || !File.Exists(PackagePath))
            return;

        try
        {
            var manager = new PackageManager();
            var expected = typeof(ContextMenuPackage).Assembly.GetName().Version;
            var installed = Find(manager);
            // The package version is the product version: a new version means NetHop was upgraded.
            if (installed is not null && installed.Id.Version is var v && new Version(v.Major, v.Minor, v.Build, v.Revision) == expected)
                return;

            var options = new AddPackageOptions
            {
                ExternalLocationUri = new Uri(AppContext.BaseDirectory),
                ForceUpdateFromAnyVersion = true,
            };
            // Builds without code signing ship an unsigned package (Windows' unsigned-publisher OID). Windows 11
            // accepts it only where unsigned packages are allowed; a signed package ignores the option.
            typeof(AddPackageOptions).GetProperty("AllowUnsigned")?.SetValue(options, true);

            var result = await manager.AddPackageByUriAsync(new Uri(PackagePath), options);
            if (result.ExtendedErrorCode is { } error)
                CrashLog.Write("Context menu package", $"{result.ErrorText} (0x{error.HResult:X8})");
        }
        catch (Exception ex)
        {
            CrashLog.Write("Context menu package", ex);
        }
    });

    /// <summary>Unregisters the package for the current user (uninstall). Never throws.</summary>
    public static void Remove()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;

        try
        {
            var manager = new PackageManager();
            if (Find(manager) is { } package)
                manager.RemovePackageAsync(package.Id.FullName).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            CrashLog.Write("Context menu package removal", ex);
        }
    }

    private static Windows.ApplicationModel.Package? Find(PackageManager manager) =>
        // An empty user SID means the current user.
        manager.FindPackagesForUser(string.Empty).FirstOrDefault(p => p.Id.Name == PackageName);
}
