param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $GroundKitRegistryArgs
)

$project = Join-Path $PSScriptRoot "src/groundkit-registry/GroundKit.Registry.csproj"
dotnet run --project $project --no-restore -v:q -p:NoWarn=NU1510%3BNU1901 -- @GroundKitRegistryArgs
exit $LASTEXITCODE