Set-StrictMode -Version Latest

function New-SecureWorkingDirectory {
    param([string]$BasePath = [IO.Path]::GetTempPath())

    $path = Join-Path $BasePath ("neo-n4-private-network-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $path -ErrorAction Stop | Out-Null

    try {
        if ($IsWindows) {
            $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
            if ($null -eq $identity) {
                throw "Unable to resolve the current Windows identity for temporary-directory ACLs."
            }
            $acl = Get-Acl -LiteralPath $path
            $acl.SetAccessRuleProtection($true, $false)
            $rule = [Security.AccessControl.FileSystemAccessRule]::new(
                $identity,
                [Security.AccessControl.FileSystemRights]::FullControl,
                [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
                    [Security.AccessControl.InheritanceFlags]::ObjectInherit,
                [Security.AccessControl.PropagationFlags]::None,
                [Security.AccessControl.AccessControlType]::Allow)
            $acl.SetAccessRule($rule)
            Set-Acl -LiteralPath $path -AclObject $acl
        }
        else {
            & chmod 700 $path
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to restrict temporary working directory permissions: $path"
            }
        }
    }
    catch {
        Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
        throw
    }

    return (Resolve-Path -LiteralPath $path).Path
}

function Assert-NoReparsePoints {
    param([string]$Path)

    $items = @((Get-Item -LiteralPath $Path -Force)) +
        @(Get-ChildItem -LiteralPath $Path -Force -Recurse)
    foreach ($item in $items) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Operator deployment staging rejects links and junctions: $($item.FullName)"
        }
    }
}

function Copy-ApprovedTreeFiles {
    param(
        [string]$SourceRoot,
        [string]$DestinationRoot,
        [scriptblock]$Include
    )

    Assert-NoReparsePoints -Path $SourceRoot
    foreach ($file in Get-ChildItem -LiteralPath $SourceRoot -Force -File -Recurse) {
        if (-not (& $Include $file)) {
            continue
        }
        $relativePath = [IO.Path]::GetRelativePath($SourceRoot, $file.FullName)
        $destinationPath = Join-Path $DestinationRoot $relativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $destinationPath) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destinationPath -Force
    }
}

function Copy-OperatorDeployment {
    param(
        [string]$SourceExecutable,
        [string]$ReviewedConfig,
        [string]$DestinationDirectory,
        [string]$RequiredPluginAssembly,
        [string]$RequiredPluginConfig
    )

    $sourcePath = (Resolve-Path -LiteralPath $SourceExecutable).Path
    $reviewedConfigPath = (Resolve-Path -LiteralPath $ReviewedConfig).Path
    $sourceItem = Get-Item -LiteralPath $sourcePath -Force
    $reviewedConfigItem = Get-Item -LiteralPath $reviewedConfigPath -Force
    if ($sourceItem.PSIsContainer) {
        throw "Operator executable must be a file: $sourcePath"
    }
    if ($reviewedConfigItem.PSIsContainer) {
        throw "Reviewed operator config must be a file: $reviewedConfigPath"
    }
    if (($sourceItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Operator executable must not be a link or junction: $sourcePath"
    }
    if (($reviewedConfigItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Reviewed operator config must not be a link or junction: $reviewedConfigPath"
    }

    $sourceDirectory = Split-Path -Parent $sourcePath
    $sourceExecutableName = Split-Path -Leaf $sourcePath
    if (Test-Path -LiteralPath $DestinationDirectory) {
        throw "Operator deployment destination must not already exist: $DestinationDirectory"
    }
    New-Item -ItemType Directory -Path $DestinationDirectory | Out-Null

    foreach ($file in Get-ChildItem -LiteralPath $sourceDirectory -Force -File) {
        $allowed = -not $file.Name.StartsWith(".", [StringComparison]::Ordinal) `
            -and ($file.Name -eq $sourceExecutableName `
                -or $file.Extension -in @(".dll", ".exe", ".so", ".dylib") `
                -or $file.Name.EndsWith(".deps.json", [StringComparison]::OrdinalIgnoreCase) `
                -or $file.Name.EndsWith(".runtimeconfig.json", [StringComparison]::OrdinalIgnoreCase))
        if ($allowed) {
            if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Operator runtime file must not be a link or junction: $($file.FullName)"
            }
            Copy-Item -LiteralPath $file.FullName -Destination $DestinationDirectory -Force
        }
    }

    $runtimeDirectory = Join-Path $sourceDirectory "runtimes"
    if (Test-Path -LiteralPath $runtimeDirectory -PathType Container) {
        Copy-ApprovedTreeFiles -SourceRoot $runtimeDirectory `
            -DestinationRoot (Join-Path $DestinationDirectory "runtimes") `
            -Include {
                param($file)
                $relativePath = [IO.Path]::GetRelativePath($runtimeDirectory, $file.FullName)
                $hasHiddenSegment = @(
                    $relativePath -split '[\\/]' |
                        Where-Object { $_.StartsWith(".", [StringComparison]::Ordinal) }
                ).Count -gt 0
                -not $hasHiddenSegment `
                    -and $file.Extension -in @(".dll", ".exe", ".so", ".dylib")
            }
    }

    $pluginDirectory = Join-Path $sourceDirectory (Join-Path "Plugins" $RequiredPluginAssembly)
    if (-not (Test-Path -LiteralPath $pluginDirectory -PathType Container)) {
        throw "Required plugin deployment not found: $pluginDirectory"
    }
    $pluginDestination = Join-Path $DestinationDirectory (Join-Path "Plugins" $RequiredPluginAssembly)
    Copy-ApprovedTreeFiles -SourceRoot $pluginDirectory -DestinationRoot $pluginDestination -Include {
        param($file)
        $relativePath = [IO.Path]::GetRelativePath($pluginDirectory, $file.FullName)
        $hasHiddenSegment = @(
            $relativePath -split '[\\/]' |
                Where-Object { $_.StartsWith(".", [StringComparison]::Ordinal) }
        ).Count -gt 0
        -not $hasHiddenSegment `
            -and ($file.Name -eq $RequiredPluginConfig `
                -or $file.Extension -in @(".dll", ".exe", ".so", ".dylib") `
                -or $file.Name.EndsWith(".deps.json", [StringComparison]::OrdinalIgnoreCase) `
                -or $file.Name.EndsWith(".runtimeconfig.json", [StringComparison]::OrdinalIgnoreCase))
    }

    Copy-Item -LiteralPath $reviewedConfigPath `
        -Destination (Join-Path $DestinationDirectory "config.json") -Force
    $destinationExecutable = Join-Path $DestinationDirectory $sourceExecutableName
    if (-not (Test-Path -LiteralPath $destinationExecutable -PathType Leaf)) {
        throw "Failed to stage operator executable: $destinationExecutable"
    }
    return $destinationExecutable
}

Export-ModuleMember -Function New-SecureWorkingDirectory, Copy-OperatorDeployment
