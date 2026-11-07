// Wolvy's Cross Beams tests: the duplicate rule (Plugin.IsDuplicate) with real UnityEngine Vector3/Quaternion maths.
// Snap points are given as the game's pieces have them: a 2 m beam has one at each end, a pole at top and bottom,
// a square floor at its four corners. "Existing" is the placed piece (no rotation); "ghost" is the new one.
using System; using System.Collections.Generic; using System.Reflection; using UnityEngine;
class T {
 static int fails=0; static MethodInfo dup;
 static void Check(string n,bool got,bool want){Console.WriteLine((got==want?"PASS ":"FAIL ")+n+" -> "+(got?"refused (duplicate)":"snaps")); if(got!=want)fails++;}
 static Quaternion Yaw(float deg){float h=deg*Mathf.Deg2Rad/2; return new Quaternion(0,(float)Math.Sin(h),0,(float)Math.Cos(h));}
 static Quaternion Roll(float deg){float h=deg*Mathf.Deg2Rad/2; return new Quaternion(0,0,(float)Math.Sin(h),(float)Math.Cos(h));}
 static List<Vector3> L(params float[] v){var l=new List<Vector3>(); for(int i=0;i<v.Length;i+=3) l.Add(new Vector3(v[i],v[i+1],v[i+2])); return l;}
 static bool D(Quaternion r, List<Vector3> local){ // existing piece at origin, unrotated: its world snaps = its local snaps
   var g=new List<Vector3>(); foreach(var v in local) g.Add(r*v); return (bool)dup.Invoke(null,new object[]{g,new List<Vector3>(local)});}
 static void Main(){
  dup=Assembly.LoadFrom("WolvysCrossBeams.dll").GetType("WolvysCrossBeams.Plugin",true).GetMethod("IsDuplicate",BindingFlags.Static|BindingFlags.NonPublic);
  var beam=L(1,0,0, -1,0,0); var diag=L(1,1,0, -1,-1,0); var pole=L(0,1,0, 0,-1,0); var floor=L(1,0,1, 1,0,-1, -1,0,1, -1,0,-1);
  Check("beam, same angle (double click)", D(Yaw(0),beam), true);
  Check("beam turned 180 (looks identical)", D(Yaw(180),beam), true);
  Check("horizontal + cross (beam turned 90)", D(Yaw(90),beam), false);
  Check("diagonal X cross (45 beam turned 180)", D(Yaw(180),diag), false);
  Check("X cross in a wall (beam rolled 90)", D(Roll(90),diag), false);
  Check("diagonal, same angle", D(Yaw(0),diag), true);
  Check("pole turned 37 (looks identical)", D(Yaw(37),pole), true);
  Check("square floor turned 90 (looks identical)", D(Yaw(90),floor), true);
  Check("square floor turned 45", D(Yaw(45),floor), false);
  Check("beam 3 cm off a duplicate (within 5 cm: still refused)", (bool)dup.Invoke(null,new object[]{L(1.03f,0,0, -0.97f,0,0),beam}), true);
  Check("beam 10 cm along (not a duplicate)", (bool)dup.Invoke(null,new object[]{L(1.1f,0,0, -0.9f,0,0),beam}), false);
  Check("piece without snap points: vanilla rule", (bool)dup.Invoke(null,new object[]{new List<Vector3>(),beam}), true);
  Console.WriteLine(fails==0?"ALL PASSED":"FAILURES: "+fails); Environment.Exit(fails);
 }}
