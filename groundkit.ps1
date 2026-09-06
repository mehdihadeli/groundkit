param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $GroundKitArgs
)

$project = Join-Path $PSScriptRoot "src/groundkit/GroundKit.csproj"
dotnet run --project $project --no-restore -v:q -- @GroundKitArgs
exit $LASTEXITCODE
