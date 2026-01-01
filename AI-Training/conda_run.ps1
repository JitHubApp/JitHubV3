param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$EnvName,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$CommandArgs
)

$ErrorActionPreference = 'Stop'

function Get-CondaExe {
    if ($env:CONDA_EXE -and (Test-Path $env:CONDA_EXE)) {
        return $env:CONDA_EXE
    }

    $cmd = Get-Command conda -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Source) {
        return $cmd.Source
    }

    return $null
}

$condaExe = Get-CondaExe
if (-not $condaExe) {
    Write-Error "conda was not found. Open an 'Anaconda Prompt' / 'Miniconda Prompt', or ensure conda is on PATH." 
    exit 1
}

if (-not $CommandArgs -or $CommandArgs.Count -eq 0) {
    Write-Error "No command provided. Example: .\\conda_run.ps1 lora-env python train.py --help"
    exit 2
}

& $condaExe run -n $EnvName --no-capture-output @CommandArgs
exit $LASTEXITCODE
