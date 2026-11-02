"""Enforce production project dependencies and keep business layers transport-free."""

from pathlib import Path
import re
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
prefix = 'ECommerceStore.Pact.'
allowed = {
    'Domain': set(),
    'Application': {'Domain'},
    'Infrastructure': {'Application'},
    'Api': {'Application', 'Infrastructure'},
}
errors = []
for layer, references in allowed.items():
    folder = root / 'src' / f'{prefix}{layer}'
    project = ET.parse(folder / f'{prefix}{layer}.csproj')
    actual = {
        Path(reference.attrib['Include']).stem.removeprefix(prefix)
        for reference in project.iter('ProjectReference')
    }
    if actual != references:
        errors.append(f'{layer}: expected project references {sorted(references)}, found {sorted(actual)}')
    if layer in {'Domain', 'Application'}:
        if list(project.iter('PackageReference')):
            errors.append(f'{layer}: external package dependencies require an explicit architecture review')
        for source in folder.rglob('*.cs'):
            if {'bin', 'obj'} & set(source.parts):
                continue
            imports = re.findall(r'^using\s+(?:static\s+)?([^;]+);', source.read_text(), re.MULTILINE)
            if any(re.search(r'MongoDB|Microsoft\.AspNetCore|ECommerceStore\.Pact\.(?:Api|Infrastructure)', value)
                   for value in imports):
                errors.append(f'{source.relative_to(root)}: forbidden transport or persistence import')
if errors:
    raise SystemExit('\n'.join(errors))
print('Production project boundaries passed.')
