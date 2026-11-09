param(
    [Parameter(Mandatory)]
    [string]$GeneratorJar
)

$ErrorActionPreference = 'Stop'
$generator = (Resolve-Path -LiteralPath $GeneratorJar).Path
$version = & java -jar $generator version
if ($LASTEXITCODE -ne 0 -or $version -ne '7.27.0') {
    throw 'CropPilot requires OpenAPI Generator 7.27.0.'
}

$backend = Split-Path -Parent $PSScriptRoot
$root = Split-Path -Parent $backend
$contract = Join-Path $root 'docs/openapi/openapi.yaml'
$output = Join-Path $backend 'CropPilot.Api/Generated'

& java -jar $generator generate -g aspnetcore -i $contract -o $output `
    --additional-properties 'packageName=CropPilot.Contract,sourceFolder=.,buildTarget=library,classModifier=abstract,operationModifier=abstract,operationResultTask=true,aspnetCoreVersion=10.0' `
    --global-property 'apis=Auth,models=LoginRequest:ApiProblem:ApiValidationProblem,supportingFiles=ValidateModelStateAttribute.cs:CustomEnumConverter.cs,apiTests=false,modelTests=false,apiDocs=false,modelDocs=false'
if ($LASTEXITCODE -ne 0) {
    throw 'OpenAPI generation failed.'
}
