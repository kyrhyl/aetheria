param(
    [int]$Count = 2,
    [string]$ExePath = "..\build\Aetheria.exe"
)

$resolvedExe = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot $ExePath)

for ($i = 1; $i -le $Count; $i++) {
    $x = 80 + (($i - 1) * 460)
    $y = 80
    $profile = "client$i"
    $instance = "Client-$i"

    $args = @(
        "--profile=$profile",
        "--instance=$instance",
        "--x=$x",
        "--y=$y",
        "--w=440",
        "--h=760"
    )

    Start-Process -FilePath $resolvedExe -ArgumentList $args
}
