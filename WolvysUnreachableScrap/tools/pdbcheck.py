import struct, sys
def dll_ids(path):
    d=open(path,'rb').read()
    pe=struct.unpack_from('<I',d,0x3c)[0]; nsec=struct.unpack_from('<H',d,pe+6)[0]; optsz=struct.unpack_from('<H',d,pe+20)[0]
    opt=pe+24; magic=struct.unpack_from('<H',d,opt)[0]; dd=opt+(96 if magic==0x10b else 112)
    rva,size=struct.unpack_from('<II',d,dd+6*8); secs=opt+optsz
    def off(r):
        for i in range(nsec):
            s=secs+i*40; va,vs,raw=struct.unpack_from('<I',d,s+12)[0],struct.unpack_from('<I',d,s+8)[0],struct.unpack_from('<I',d,s+20)[0]
            if va<=r<va+max(vs,1): return r-va+raw
    o=off(rva); out=[]
    for i in range(size//28):
        e=o+i*28; typ=struct.unpack_from('<I',d,e+12)[0]; major,minor=struct.unpack_from('<HH',d,e+8); ptr=struct.unpack_from('<I',d,e+24)[0]
        if typ==2 and d[ptr:ptr+4]==b'RSDS':
            out.append(('codeview',d[ptr+4:ptr+20].hex(),struct.unpack_from('<I',d,e+4)[0],major,minor,d[ptr+24:d.index(b'\0',ptr+24)].decode()))
    return out
def pdb_id(path):
    d=open(path,'rb').read()
    assert d[:4]==b'BSJB', 'not a portable pdb'
    vlen=struct.unpack_from('<I',d,12)[0]; p=16+vlen+2; n=struct.unpack_from('<H',d,p)[0]; p+=2
    for _ in range(n):
        off,size=struct.unpack_from('<II',d,p); p+=8; e=d.index(b'\0',p); name=d[p:e].decode(); p=(e+4)&~3
        if name=='#Pdb': return d[off:off+16].hex(), struct.unpack_from('<I',d,off+16)[0]
dll,pdb=sys.argv[1],sys.argv[2]
cv=dll_ids(dll); pid=pdb_id(pdb)
print('DLL codeview:',cv); print('PDB id:',pid)
ok=any(c[1]==pid[0] and c[2]==pid[1] and c[3]==0x0100 for c in cv)
print('MATCH (portable, guid+stamp)' if ok else 'NO MATCH')
sys.exit(0 if ok else 1)
