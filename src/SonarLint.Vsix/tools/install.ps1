﻿param($installPath, $toolsPath, $package, $project)

if ($project.DTE.Version -ne '14.0')
{
    throw 'The package can only be installed on Visual Studio 2015.'
}

if ($project.Object.AnalyzerReferences -eq $null)
{
	throw 'The package cannot be installed as an analyzer reference.'
}

$analyzersPath = split-path -path $toolsPath -parent
$analyzersPath = join-path $analyzersPath "analyzers"
$analyzersPath = join-path $analyzersPath "dotnet"

$analyzerFilePath = join-path $analyzersPath "SonarLint.dll"
$project.Object.AnalyzerReferences.Add($analyzerFilePath)