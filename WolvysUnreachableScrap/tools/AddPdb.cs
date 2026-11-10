using System; using System.IO; using Mono.Cecil; using Mono.Cecil.Cil;
class P{static void Main(string[] a){
 var res=new DefaultAssemblyResolver(); res.AddSearchDirectory(Path.GetDirectoryName(a[0])); for(int i=2;i<a.Length;i++) res.AddSearchDirectory(a[i]);
 var asm=AssemblyDefinition.ReadAssembly(a[0],new ReaderParameters{ReadSymbols=false,AssemblyResolver=res});
 asm.Write(a[1],new WriterParameters{WriteSymbols=true,SymbolWriterProvider=new PortablePdbWriterProvider()});
 var back=AssemblyDefinition.ReadAssembly(a[1],new ReaderParameters{ReadSymbols=true,AssemblyResolver=res});
 Console.WriteLine("ok, symbols read back: "+back.MainModule.HasSymbols);}}
