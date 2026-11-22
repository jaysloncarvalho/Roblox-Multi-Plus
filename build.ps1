[CmdletBinding(DefaultParameterSetName = 'Isolated')]
param(
    [Parameter(ParameterSetName = 'Isolated')]
    [string]$OutputDirectory,

    [Parameter(Mandatory, ParameterSetName = 'ProjectData')]
    [switch]$UseProjectData
)
$ErrorActionPreference = 'Stop'

$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) {
    throw 'O compilador do .NET Framework 4.8 (64 bits) não foi encontrado.'
}

$references = @(
    'WPF\WindowsBase.dll'
    'WPF\PresentationCore.dll'
    'WPF\PresentationFramework.dll'
    'WPF\UIAutomationTypes.dll'
    'WPF\UIAutomationProvider.dll'
    'System.Xaml.dll'
    'System.Security.dll'
    'System.Web.Extensions.dll'
    'System.Net.Http.dll'
    'System.Core.dll'
    'System.dll'
    'System.Xml.dll'
) | ForEach-Object { '/reference:' + (Join-Path $framework $_) }

$webViewVersion = '1.0.4370.26'
$webViewHash = '1C0DACAC84B70109B3FD42F90C53A0BA942ABC3A87590D7A4E0CCD86E4A06CF4'
$cache = Join-Path $PSScriptRoot '.dependencies'
[void][IO.Directory]::CreateDirectory($cache)
$archive = Join-Path $cache "webview2-$webViewVersion.zip"
$package = Join-Path $cache "webview2-$webViewVersion"
if (!(Test-Path -LiteralPath $archive)) {
    $uri = "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$webViewVersion/microsoft.web.webview2.$webViewVersion.nupkg"
    $tempArchive = $archive + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        Invoke-WebRequest -UseBasicParsing -Uri $uri -OutFile $tempArchive
        if ((Get-FileHash -LiteralPath $tempArchive).Hash -ne $webViewHash) { throw 'Pacote WebView2 inválido.' }
        Move-Item -LiteralPath $tempArchive -Destination $archive
    } finally {
        if (Test-Path -LiteralPath $tempArchive) { Remove-Item -LiteralPath $tempArchive }
    }
}
if ((Get-FileHash -LiteralPath $archive).Hash -ne $webViewHash) { throw 'O pacote WebView2 em cache não confere.' }
Expand-Archive -LiteralPath $archive -DestinationPath $package -Force
$webViewFiles = @(
    (Join-Path $package 'lib\net462\Microsoft.Web.WebView2.Core.dll')
    (Join-Path $package 'lib\net462\Microsoft.Web.WebView2.Wpf.dll')
    (Join-Path $package 'runtimes\win-x64\native\WebView2Loader.dll')
)
$references += $webViewFiles | Select-Object -First 2 | ForEach-Object { '/reference:' + $_ }


if (!$OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot 'bin' }
$binaryName = 'Roblox Multi by Jayy.exe'
if ($UseProjectData) {
    $OutputDirectory = $PSScriptRoot
    $binaryName = 'Roblox Multi by Jayy - Nova UI v2.exe'
}

[void][IO.Directory]::CreateDirectory($OutputDirectory)
$OutputDirectory = (Get-Item -LiteralPath $OutputDirectory).FullName
if ($OutputDirectory -eq $PSScriptRoot -and !$UseProjectData) {
    throw 'Use -UseProjectData para compilar ao lado do executável original.'
}
$output = Join-Path $OutputDirectory $binaryName
$theme = Join-Path $PSScriptRoot 'Themes\Theme.xaml'
$sources = @(
    (Join-Path $PSScriptRoot 'RobloxMultiByJayy.cs')
    (Join-Path $PSScriptRoot 'Views\MainWindow.cs')
    (Join-Path $PSScriptRoot 'Views\AccountsView.cs')
    (Join-Path $PSScriptRoot 'Views\AccountDialogs.cs')
    (Join-Path $PSScriptRoot 'Views\SettingsViews.cs')
    (Join-Path $PSScriptRoot 'Views\LogsView.cs')
    (Join-Path $PSScriptRoot 'Views\DiscordPresence.cs')
    (Join-Path $PSScriptRoot 'Services\WebViewLogin.cs')
    (Join-Path $PSScriptRoot 'Services\SafeDiagnostics.cs')
    (Join-Path $PSScriptRoot 'Services\VaultProtection.cs')
    (Join-Path $PSScriptRoot 'Services\AccountStore.cs')
    (Join-Path $PSScriptRoot 'Services\ProcessPerformance.cs')
    (Join-Path $PSScriptRoot 'Services\SessionLifecycle.cs')
    (Join-Path $PSScriptRoot 'Services\DiscordRpcManager.cs')
)
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$output" "/resource:$theme,RobloxMultiByJayy.Theme.xaml" @references @sources
if ($LASTEXITCODE -ne 0) { throw "Falha na compilação (código $LASTEXITCODE)." }

foreach ($file in $webViewFiles) { Copy-Item -LiteralPath $file -Destination $OutputDirectory -Force }
Copy-Item -LiteralPath (Join-Path $package 'LICENSE.txt') -Destination (Join-Path $OutputDirectory 'WebView2-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $package 'NOTICE.txt') -Destination (Join-Path $OutputDirectory 'WebView2-NOTICE.txt') -Force
Write-Output $output
