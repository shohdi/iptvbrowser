# Build and install Shohdi.Iptv

Use a signed **Release | x64** sideload bundle for Windows and Xbox Developer Mode. Visual Studio's **Build** and **Deploy** commands can refresh the development copy without updating the installer in `AppPackages`.

## Before each package build

- Install Visual Studio 2022 with UWP development tools and Windows SDK **10.0.22621.0**.
- After saving your code changes, increase the `Identity Version` in [Package.appxmanifest](Package.appxmanifest). The current package is **1.0.3.0**. Use **1.0.4.0** for the next package if that is higher than the version already installed. Keep the package name and publisher unchanged so the new package can update the existing app.
- Use a code-signing certificate with a private key whose subject matches the manifest's `Publisher`. Reuse your existing signing certificate when possible.

## Create the package in Visual Studio

1. Open [IptvXbox.sln](../IptvXbox.sln) and select **Release** and **x64**.
2. Right-click the **IptvXbox.App** project and choose **Publish > Create App Packages**. Choose **Sideloading**, then select the matching signing certificate from your certificate store or a `.pfx` file.
3. Set the output to the project's `AppPackages` folder. Select **x64** with the **Release** configuration, set **Generate app bundle** to **Always**, and confirm the version matches the manifest. Clear automatic version incrementing if it is selected.
4. Click **Create** and open the `*_Test` folder for the version you just built. It contains the bundle, the PowerShell installer, the public certificate, and the dependencies. [Microsoft's Visual Studio packaging guide](https://learn.microsoft.com/en-us/windows/msix/package/packaging-uwp-apps)

## Create the package from PowerShell

Open **Developer PowerShell for VS 2022** in the `IptvXbox.App` project folder. Find the signing certificate by matching the manifest publisher.

```powershell
$publisher = ([xml](Get-Content -Raw .\Package.appxmanifest)).Package.Identity.Publisher
$now = Get-Date
Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Where-Object { $_.Subject -eq $publisher -and $_.HasPrivateKey -and
        $_.NotBefore -le $now -and $_.NotAfter -gt $now } |
    Format-Table FriendlyName, Thumbprint, NotAfter
```

If the list is empty, import your signing `.pfx` into **Current User > Personal**, or create a certificate for local testing and use the returned thumbprint. [Microsoft's signing certificate guide](https://learn.microsoft.com/en-us/windows/msix/package/create-certificate-package-signing)

```powershell
New-SelfSignedCertificate -Type Custom -Subject $publisher -KeyUsage DigitalSignature `
    -CertStoreLocation 'Cert:\CurrentUser\My' -FriendlyName 'Shohdi.Iptv test signing' `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
```

Replace the placeholder with your signing certificate's thumbprint and rebuild.

```powershell
$thumbprint = 'YOUR_CERTIFICATE_THUMBPRINT'
msbuild .\IptvXbox.App.csproj /restore /t:Rebuild /p:Configuration=Release /p:Platform=x64 `
    /p:GenerateAppxPackageOnBuild=true /p:UapAppxPackageBuildMode=SideloadOnly `
    /p:AppxBundle=Always /p:AppxBundlePlatforms=x64 /p:AppxAutoIncrementPackageRevision=false `
    /p:AppxPackageSigningEnabled=true "/p:PackageCertificateThumbprint=$thumbprint" `
    /p:GenerateTemporaryStoreCertificate=false /p:AppxLogTelemetryFromSideloadingScript=false
if ($LASTEXITCODE -ne 0) { throw "Package build failed (exit code $LASTEXITCODE)." }
```

Use the `AppPackages\IptvXbox.App_<version>_Test` folder from the successful build. Keep its contents together when copying the installer to another PC.

## Install on Windows

Run the generated installer from the project folder in **Windows PowerShell**. Replace the version in the path after each new build. The installer adds the required frameworks and can prompt for administrator approval to trust the signing certificate on first install.

```powershell
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File '.\AppPackages\IptvXbox.App_1.0.3.0_Test\Install.ps1'
Get-AppxPackage -Name 'shohdi.Shohdi.Iptv' | Select-Object Name, Version, InstallLocation
```

Check that `Version` matches the new package. An `InstallLocation` under the project's `bin\x64\Release\AppX` points to the Visual Studio deployment. If Windows reports a package conflict, check the installed version first. If you need to uninstall the development copy, save any app settings you need before uninstalling.

## Install on Xbox

1. In Xbox **Developer Mode**, enable **Xbox Device Portal** under **Dev Home > Remote Access Settings**. On the PC, open the portal URL shown in Dev Home and sign in. [Xbox Device Portal setup](https://learn.microsoft.com/en-us/previous-versions/windows/uwp/xbox-apps/device-portal-xbox)
2. On the portal's **Home** page, add the `.msixbundle` from the matching `*_Test` folder and supply the framework packages from `Dependencies\x64`.
3. Install the bundle, then launch **Shohdi.Iptv** from Dev Home. [Microsoft's Xbox sideloading guide](https://learn.microsoft.com/en-us/windows/uwp/apps-for-xbox/development-options)

For direct development deployment, select **Remote Machine** and **x64** in Visual Studio, enter the Xbox IP address, and pair with **Universal (Unencrypted Protocol)** on your local network. [Microsoft's Visual Studio deployment guide for Xbox](https://learn.microsoft.com/en-us/previous-versions/windows/uwp/xbox-apps/getting-started)
