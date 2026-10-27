[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$ModRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$failures = New-Object 'System.Collections.Generic.List[string]'
$legacyPrefix = [string]::Concat('D', 'HM')
$scriptRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Join-Path $scriptRoot '..'
}
if ([string]::IsNullOrWhiteSpace($ModRoot)) {
    $ModRoot = Join-Path $scriptRoot '..\GAMMA hit sounds'
}

function Add-Failure {
    param([string]$Message)
    $script:failures.Add($Message)
}

function Read-XmlDocument {
    param([string]$Path)

    $settings = New-Object System.Xml.XmlReaderSettings
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create($Path, $settings)
    try {
        $document = New-Object System.Xml.XmlDocument
        $document.XmlResolver = $null
        $document.Load($reader)
        return $document
    }
    finally {
        $reader.Dispose()
    }
}

try {
    $RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
    $ModRoot = (Resolve-Path -LiteralPath $ModRoot).Path
}
catch {
    Write-Error "Could not resolve the repository or mod root: $_"
    exit 1
}

$xmlFiles = @(Get-ChildItem -LiteralPath $ModRoot -Filter '*.xml' -File -Recurse)
foreach ($xmlFile in $xmlFiles) {
    try {
        [void](Read-XmlDocument -Path $xmlFile.FullName)
    }
    catch {
        Add-Failure "Invalid XML in '$($xmlFile.FullName)': $_"
    }
}

$soundDefFiles = @(Get-ChildItem -LiteralPath (Join-Path $ModRoot 'Defs') -Filter '*.xml' -File -Recurse -ErrorAction SilentlyContinue)
$soundDefNames = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
$referencedFolders = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
$soundsRoot = [System.IO.Path]::GetFullPath((Join-Path $ModRoot 'Sounds'))
$clipFolderCount = 0
$clipCount = 0

foreach ($xmlFile in $soundDefFiles) {
    try {
        $document = Read-XmlDocument -Path $xmlFile.FullName
        foreach ($defNameNode in $document.SelectNodes('//SoundDef/defName')) {
            $defName = $defNameNode.InnerText.Trim()
            if ([string]::IsNullOrWhiteSpace($defName)) {
                Add-Failure "An empty SoundDef defName was found in '$($xmlFile.FullName)'."
            }
            elseif (-not $soundDefNames.Add($defName)) {
                Add-Failure "Duplicate SoundDef defName '$defName' was found."
            }
        }

        foreach ($folderNode in $document.SelectNodes('//clipFolderPath')) {
            $clipFolderCount++
            $relativePath = $folderNode.InnerText.Trim()
            if ([string]::IsNullOrWhiteSpace($relativePath) -or $relativePath -match '^(?:[A-Za-z]:|[/\\])' -or $relativePath -split '[/\\]' -contains '..') {
                Add-Failure "Unsafe or empty clipFolderPath '$relativePath' in '$($xmlFile.FullName)'."
                continue
            }

            $segments = @($relativePath -split '[/\\]' | Where-Object { $_ -ne '' })
            $currentPath = $soundsRoot
            $pathCaseMatches = $true
            foreach ($segment in $segments) {
                $matchingDirectory = Get-ChildItem -LiteralPath $currentPath -Directory -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -ceq $segment } |
                    Select-Object -First 1
                if ($null -eq $matchingDirectory) {
                    $pathCaseMatches = $false
                    break
                }
                $currentPath = $matchingDirectory.FullName
            }

            if (-not $pathCaseMatches) {
                Add-Failure "clipFolderPath '$relativePath' does not match an existing Sounds folder with exact casing."
                continue
            }

            $currentPath = [System.IO.Path]::GetFullPath($currentPath)
            $soundsPrefix = $soundsRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
            if (-not $currentPath.StartsWith($soundsPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                Add-Failure "clipFolderPath '$relativePath' resolves outside the Sounds directory."
                continue
            }

            [void]$referencedFolders.Add($currentPath)
            $clips = @(Get-ChildItem -LiteralPath $currentPath -Filter '*.ogg' -File)
            if ($clips.Count -eq 0) {
                Add-Failure "clipFolderPath '$relativePath' contains no OGG clips."
                continue
            }

            $clipCount += $clips.Count
            foreach ($clip in $clips) {
                if ($clip.Length -lt 4) {
                    Add-Failure "OGG clip '$($clip.FullName)' is empty or too small to contain a header."
                    continue
                }

                $stream = [System.IO.File]::OpenRead($clip.FullName)
                try {
                    $header = New-Object byte[] 4
                    $bytesRead = $stream.Read($header, 0, $header.Length)
                }
                finally {
                    $stream.Dispose()
                }

                if ($bytesRead -ne 4 -or [System.Text.Encoding]::ASCII.GetString($header) -cne 'OggS') {
                    Add-Failure "OGG clip '$($clip.FullName)' has no OggS container header."
                }
            }
        }
    }
    catch {
        Add-Failure "Could not inspect sound definitions in '$($xmlFile.FullName)': $_"
    }
}

if ($clipFolderCount -eq 0) {
    Add-Failure 'No clipFolderPath entries were found under Defs.'
}

$sourcePath = Join-Path $ModRoot 'Source\GammaHitEffectsMod.cs'
try {
    $sourceText = [System.IO.File]::ReadAllText($sourcePath)
    $soundNameMatches = [System.Text.RegularExpressions.Regex]::Matches(
        $sourceText,
        'private const string \w+SoundDefName = "([^"]+)";')
    if ($soundNameMatches.Count -eq 0) {
        Add-Failure "No sound Def name constants were found in '$sourcePath'."
    }
    foreach ($match in $soundNameMatches) {
        $soundDefName = $match.Groups[1].Value
        if (-not $soundDefNames.Contains($soundDefName)) {
            Add-Failure "Sound Def name '$soundDefName' is referenced in code but not defined under Defs."
        }
    }

    $settingsNameMatch = [System.Text.RegularExpressions.Regex]::Match(
        $sourceText,
        'private const string SettingsDefName = "([^"]+)";')
    if (-not $settingsNameMatch.Success) {
        Add-Failure "No settings Def name constant was found in '$sourcePath'."
    }
    else {
        $settingsDefName = $settingsNameMatch.Groups[1].Value
        $settingsDefs = New-Object 'System.Collections.Generic.List[System.Xml.XmlElement]'
        foreach ($xmlFile in $soundDefFiles) {
            $document = Read-XmlDocument -Path $xmlFile.FullName
            foreach ($settingsNode in $document.SelectNodes('//GammaHitSoundSettingsDef')) {
                $nameNode = $settingsNode.SelectSingleNode('defName')
                if ($null -ne $nameNode -and $nameNode.InnerText.Trim() -ceq $settingsDefName) {
                    $settingsDefs.Add($settingsNode)
                }
            }
        }

        if ($settingsDefs.Count -ne 1) {
            Add-Failure "Expected one settings Def named '$settingsDefName', found $($settingsDefs.Count)."
        }
        else {
            $configuredDamageDefs = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
            foreach ($damageDefNode in $settingsDefs[0].SelectNodes('additionalBulletDamageDefs/li')) {
                $damageDefName = $damageDefNode.InnerText.Trim()
                if ([string]::IsNullOrWhiteSpace($damageDefName)) {
                    Add-Failure "The settings Def '$settingsDefName' has an empty additional bullet DamageDef name."
                }
                elseif (-not $configuredDamageDefs.Add($damageDefName)) {
                    Add-Failure "The settings Def '$settingsDefName' repeats DamageDef '$damageDefName'."
                }
            }
        }
    }
}
catch {
    Add-Failure "Could not cross-check C# sound/settings references against Def XML: $_"
}

$loadFoldersPath = Join-Path $ModRoot 'LoadFolders.xml'
try {
    $loadFolders = Read-XmlDocument -Path $loadFoldersPath
    foreach ($version in @('1.6', '1.7')) {
        $versionNode = $loadFolders.SelectSingleNode("/loadFolders/v$version")
        $paths = @($versionNode.SelectNodes('li') | ForEach-Object { $_.InnerText.Trim() })
        if (-not $paths.Contains('/') -or -not $paths.Contains($version)) {
            Add-Failure "LoadFolders.xml must load both '/' and '$version' for RimWorld $version."
        }

        $assemblyPath = Join-Path $ModRoot "$version\Assemblies\GammaHitSounds.dll"
        if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
            Add-Failure "Missing versioned assembly '$assemblyPath'."
            continue
        }

        try {
            $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($assemblyPath)
            if ($assemblyName.Name -cne 'GammaHitSounds') {
                Add-Failure "Assembly '$assemblyPath' has unexpected identity '$($assemblyName.Name)'."
            }

            $assemblyBytes = [System.IO.File]::ReadAllBytes($assemblyPath)
            $assemblyText = [System.Text.Encoding]::Unicode.GetString($assemblyBytes)
            if ($assemblyText.Contains($legacyPrefix)) {
                Add-Failure "Legacy $legacyPrefix metadata remains in '$assemblyPath'."
            }
        }
        catch {
            Add-Failure "Could not inspect assembly '$assemblyPath': $_"
        }
    }
}
catch {
    Add-Failure "Could not inspect '$loadFoldersPath': $_"
}

$legacyAssembly = Join-Path $ModRoot (Join-Path 'Assemblies' ($legacyPrefix + '.GammaHitEffects.dll'))
if (Test-Path -LiteralPath $legacyAssembly) {
    Add-Failure "Legacy assembly path still exists: '$legacyAssembly'."
}

$textExtensions = @('.cs', '.csproj', '.xml', '.md', '.txt', '.ps1', '.props')
$textFiles = Get-ChildItem -LiteralPath $RepositoryRoot -File -Recurse |
    Where-Object {
        $textExtensions -ccontains $_.Extension.ToLowerInvariant() -and
        $_.FullName -notmatch '[\\/](?:\.git|bin|obj)[\\/]'
    }
foreach ($textFile in $textFiles) {
    try {
        $content = [System.IO.File]::ReadAllText($textFile.FullName)
        if ($content.Contains($legacyPrefix)) {
            Add-Failure "Legacy $legacyPrefix prefix remains in '$($textFile.FullName)'."
        }
    }
    catch {
        Add-Failure "Could not scan '$($textFile.FullName)': $_"
    }
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        Write-Error $failure
    }
    exit 1
}

Write-Output "Validated $clipFolderCount sound folders, $clipCount OGG clips, all project XML files, version routing, and both assemblies."
