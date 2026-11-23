param([Parameter(Mandatory)][string]$Journal, [Parameter(Mandatory)][string]$NxFixture)
$ErrorActionPreference = 'Stop'

# Use runtime implementations and the compatibility facades seen by a journal.
# An SDK reference pack, or every assembly in the runtime directory, can hide missing NX references.
$runtime = [IO.Path]::GetDirectoryName([object].Assembly.Location)
$referenceNames = 'System.Private.CoreLib.dll', 'System.Runtime.dll', 'mscorlib.dll', 'System.dll', 'System.Core.dll'
$references = foreach ($name in $referenceNames) {
    [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile((Join-Path $runtime $name))
}

$parse = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithPreprocessorSymbols([string[]]@('COMPILE_FULL_PLANAR_JOURNAL'))
$trees = foreach ($file in $Journal, $NxFixture) {
    [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText(
        [IO.File]::ReadAllText($file), $parse, $file, [Text.Encoding]::UTF8, [Threading.CancellationToken]::None)
}
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create(
    'PlanarJournal', [Microsoft.CodeAnalysis.SyntaxTree[]]$trees, [Microsoft.CodeAnalysis.MetadataReference[]]$references, $options)
$buffer = [IO.MemoryStream]::new()
try {
    $result = $compilation.Emit($buffer)
    if (!$result.Success) {
        $result.Diagnostics | Where-Object Severity -eq Error | ForEach-Object { $_.ToString() }
        exit 1
    }
} finally {
    $buffer.Dispose()
}
