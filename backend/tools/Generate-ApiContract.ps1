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
$apis = 'Auth:Fields:Plantings:Overview:Materials'
$models = 'LoginRqDto:AccessTokenRsDto:LogoutRqDto:ApiProblemRsDto:ApiValidationProblemRsDto:CreateFieldRqDto:EditFieldRqDto:DeleteFieldRqDto:FieldDto:FieldRsDto:CreatePlantingRqDto:EditPlantingRqDto:PlantingRsDto:PlantingStatus:FieldPlantingsRsDto:FieldOverviewRsDto:PlantingHistoryItemDto:CreateFertilizerMaterialRqDto:FertilizerMaterialRsDto:CreateFertilizerLotRqDto:EditFertilizerLotRqDto:FertilizerLotRsDto:CreateFertilizationRqDto:EditFertilizationRqDto:FertilizationRsDto:FertilizationDetailRqDto:EditFertilizationDetailRqDto:FertilizationDetailRsDto:ApiConflictRsDto'

& java -jar $generator generate -g aspnetcore -i $contract -o $output `
    --additional-properties 'packageName=CropPilot.Contract,sourceFolder=.,buildTarget=library,classModifier=abstract,operationModifier=abstract,operationResultTask=true,aspnetCoreVersion=10.0' `
    --global-property "apis=$apis,models=$models,supportingFiles=ValidateModelStateAttribute.cs:CustomEnumConverter.cs,apiTests=false,modelTests=false,apiDocs=false,modelDocs=false"
if ($LASTEXITCODE -ne 0) {
    throw 'C# OpenAPI generation failed.'
}

$dashboardOutput = Join-Path $root 'dashboard/src/app/generated'
# JSON request bodies need arrays even when OpenAPI requires unique items.
& java -jar $generator generate -g typescript-angular -i $contract -o $dashboardOutput `
    --additional-properties 'ngVersion=22.1.0,providedIn=root,useSingleRequestParameter=true' `
    --type-mappings 'set=Array' `
    --global-property "apis=$apis,models=$models,supportingFiles=api.ts:configuration.ts:index.ts:variables.ts:encoder.ts:api.base.service.ts:query.params.ts:models.ts:api.module.ts:provide-api.ts:param.ts,apiTests=false,modelTests=false,apiDocs=false,modelDocs=false"
if ($LASTEXITCODE -ne 0) {
    throw 'Angular OpenAPI generation failed.'
}
