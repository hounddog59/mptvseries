# Makes MP-TVSeries-<version>.mpe1 from the official package by swapping in this build.
# Usage: python3 repack-mpe1.py official.mpe1 out.mpe1 MP-TVSeries.dll x86\MP-TVSeries-Configuration.exe x64\MP-TVSeries-Configuration.exe MPEI/MP-TVSeries.xmp2
# The installer details (version, author, description) come from MPEI/MP-TVSeries.xmp2.
import zipfile, sys, re, datetime
src, out, dll, exe86, exe64, xmp2 = sys.argv[1:7]
import os
links = os.path.join(os.path.dirname(os.path.abspath(xmp2)), 'OnlineLinks')  # Start-menu web links from MPEI/OnlineLinks
zin = zipfile.ZipFile(src)
xml = zin.read('MediaPortalExtension.xml').decode('utf-8-sig')
had_bom = zin.read('MediaPortalExtension.xml')[:3] == b'\xef\xbb\xbf'

DLL = 'Installer{CopyFile}/{0bc8f04c-4da4-4299-bad4-aa3444d3aa57}-MP-TVSeries.dll'
X86 = 'Installer{CopyFile}/{193d9414-f8cc-41c9-9a42-445266c8c03f}-MP-TVSeries-Configuration.exe'
X64 = 'Installer{CopyFile}/{4b9c98f4-6610-467d-bca2-bb31cf21f974}-MP-TVSeries-Configuration.exe'
PDB = 'Installer{CopyFile}/{00598103-ca29-49b2-8b5e-ae9be724c51f}-MP-TVSeries.pdb'
repl = {DLL: open(dll,'rb').read(), X86: open(exe86,'rb').read(), X64: open(exe64,'rb').read()}

# drop the old debug-symbols file (it belongs to the official build)
pdbzip = PDB.replace('/', '\\')
blocks = re.findall(r'[ \t]*<FileItem [^>]*>\s*(?:(?!</FileItem>).)*?' + re.escape(pdbzip) + r'.*?</FileItem>\r?\n', xml, re.S)
assert len(blocks) in (0, 2), len(blocks)  # 0 when the source is one of our own packages (no .pdb left)
for b in blocks: xml = xml.replace(b, '', 1)

# general info from our MP-TVSeries.xmp2
x = open(xmp2, encoding='utf-8-sig').read()
def tag(s, t):
    m = re.search(r'<GeneralInfo>.*?(<' + t + r'(?: />|>.*?</' + t + r'>))', s, re.S); assert m, t; return m.group(1)
for t in ['Author', 'HomePage', 'UpdateUrl', 'VersionDescription', 'OnlineLocation', 'DevelopmentStatus']:
    old = tag(xml, t); new = tag(x, t)
    xml = xml.replace(old, new, 1)
ver = re.search(r'<GeneralInfo>.*?(<Version>.*?</Version>)', x, re.S).group(1)
oldver = re.search(r'<GeneralInfo>.*?(<Version>.*?</Version>)', xml, re.S).group(1)
xml = xml.replace(oldver, ver.replace('\n', '\r\n') if '\r\n' in xml and '\r\n' not in ver else ver, 1)
now = datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%S')
xml = re.sub(r'<ReleaseDate>[^<]*</ReleaseDate>', '<ReleaseDate>' + now + '</ReleaseDate>', xml, count=1)
# line endings as in the original
xml = re.sub(r'(?<!\r)\n', '\r\n', xml)

zout = zipfile.ZipFile(out, 'w')
for i in zin.infolist():
    if i.filename == PDB: continue
    data = xml.encode('utf-8') if i.filename == 'MediaPortalExtension.xml' else repl.get(i.filename, zin.read(i.filename))
    base = i.filename.rsplit('}-', 1)[-1]
    if i.filename.endswith('.url') and os.path.isfile(os.path.join(links, base)):
        data = open(os.path.join(links, base), 'rb').read()
    if i.filename == 'MediaPortalExtension.xml' and had_bom: data = b'\xef\xbb\xbf' + data
    ni = zipfile.ZipInfo(i.filename, date_time=datetime.datetime.now().timetuple()[:6] if i.filename in repl or i.filename=='MediaPortalExtension.xml' else i.date_time)
    ni.compress_type = i.compress_type; ni.external_attr = i.external_attr; ni.create_system = i.create_system
    zout.writestr(ni, data)
zout.close()
print('ok', len(zin.infolist()) - 1, 'entries')
