# Checks that the portable zip holds exactly what the installer installs: every file and folder myseq.xml puts
# in the install folder, and nothing else except the portable read-me.
# Usage: check-portable.ps1 <zip path>   (run from the repository root)
param(
	[Parameter(Mandatory = $true, Position = 0)][string]$Zip
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$failures = 0
function Check([bool]$ok, [string]$what) {
	if ($ok) { Write-Host "  ok    $what" }
	else { Write-Host "  FAIL  $what"; $script:failures++ }
}

[xml]$wix = Get-Content -LiteralPath 'myseq.xml'
$ns = New-Object System.Xml.XmlNamespaceManager($wix.NameTable)
$ns.AddNamespace('w', $wix.DocumentElement.NamespaceURI)

# The path of an element inside the install folder: the names of the Directory elements above it, then its own name.
function Install-Path($node) {
	$parts = New-Object System.Collections.ArrayList
	$parts.Insert(0, $node.GetAttribute('Name'))
	$parent = $node.ParentNode
	while ($parent.LocalName -ne 'DirectoryRef') {
		if ($parent.LocalName -eq 'Directory') { $parts.Insert(0, $parent.GetAttribute('Name')) }
		$parent = $parent.ParentNode
	}
	return ($parts -join '\')
}

$expectedFiles = @($wix.SelectNodes('//w:DirectoryRef[@Id="INSTALLDIR"]//w:File', $ns) | ForEach-Object { Install-Path $_ })
$expectedFolders = @($wix.SelectNodes('//w:DirectoryRef[@Id="INSTALLDIR"]//w:Directory', $ns) | ForEach-Object { Install-Path $_ })
Check ($expectedFiles.Count -gt 10) "read $($expectedFiles.Count) files and $($expectedFolders.Count) folders from myseq.xml"

$zipPath = (Resolve-Path -LiteralPath $Zip).Path
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
	$names = @($archive.Entries | ForEach-Object { $_.FullName.Replace('/', '\') })
}
finally {
	$archive.Dispose()
}

$actualFiles = @($names | Where-Object { -not $_.EndsWith('\') } | ForEach-Object { $_.Substring($_.IndexOf('\') + 1) })
# A folder counts when it has an entry of its own (an empty folder) or a file in it (zip tools leave out the entry then).
$actualFolders = New-Object System.Collections.Generic.List[string]
foreach ($name in $names) {
	$segments = @($name.Substring($name.IndexOf('\') + 1).TrimEnd('\').Split('\'))
	$depth = if ($name.EndsWith('\')) { $segments.Count } else { $segments.Count - 1 }
	for ($i = 1; $i -le $depth; $i++) { $actualFolders.Add(($segments[0..($i - 1)] -join '\')) }
}

Check (@($names | Where-Object { -not $_.StartsWith('MySEQ\') }).Count -eq 0) 'everything is inside one MySEQ folder'
foreach ($f in $expectedFiles) { Check ($actualFiles -contains $f) "file $f" }
foreach ($d in $expectedFolders) { Check ($actualFolders -contains $d) "folder $d" }
$extra = @($actualFiles | Where-Object { ($expectedFiles -notcontains $_) -and $_ -ne 'README-portable.txt' })
Check ($extra.Count -eq 0) ("no files the installer does not install" + $(if ($extra.Count) { ": " + ($extra -join ', ') } else { '' }))
Check ($actualFiles -contains 'README-portable.txt') 'the portable read-me is there'

Write-Host ""
Write-Host $(if ($failures -eq 0) { "PASSED" } else { "FAILED ($failures)" })
exit $(if ($failures -eq 0) { 0 } else { 1 })
