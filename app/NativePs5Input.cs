using System;using System.IO;using System.Diagnostics;
internal sealed partial class TizenPspApp {
 readonly NativePadsReader ps5Pads=new NativePadsReader(null,3);
 readonly DualSenseButtonMap ps5Map=new DualSenseButtonMap(PspInputMap.Bits);
 int ps5Owner=-1;uint ps5Previous;bool ps5Refresh=true;
 void InitializeNativePs5(){
  mappingPath=Path.Combine(Tizen.Applications.Application.Current.DirectoryInfo.Data,"dualsense-native-map.txt");
  try{if(File.Exists(mappingPath))ps5Map.Load(File.ReadAllText(mappingPath));}catch(Exception e){Log("PS5 map defaults: "+e.Message);}
  SyncPs5Map();ps5Pads.Enabled=true;ps5Pads.Start();
 }
 void SyncPs5Map(){inputMap=new PspInputMap();for(int i=0;i<ps5Map.Sources.Length;i++)inputMap.Bindings[i]="button:"+ps5Map.Sources[i];ps5Refresh=true;}
 void PumpNativePs5(){
  ps5Pads.Enabled=!paused&&!terminating;if(paused||terminating)return;
  long stamp=Stopwatch.GetTimestamp();
  if(ps5Owner<0||!ps5Pads.Slots.Read(ps5Owner,stamp,false).Connected){ps5Owner=-1;for(int i=0;i<3;i++)if(ps5Pads.Slots.Read(i,stamp,false).Connected){ps5Owner=i;break;}}
  // PSP has one local player. Choose the controller used in the library; keep it
  // through gameplay so another paired pad cannot steal the running game.
  if(game==null||mapperVisible)for(int i=0;i<3;i++)if(ps5Pads.Slots.Read(i,stamp,false).Bits!=0){ps5Owner=i;break;}
  var p=ps5Owner<0?new PhysicalPadState():ps5Pads.Slots.Read(ps5Owner,stamp,true);
  uint pressed=p.Bits&~ps5Previous;double now=ControllerReader.Now;
  if(p.Bits!=ps5Previous||p.Bits!=0){lastRaw=now;rawTimes.Enqueue(now);while(rawTimes.Count>512)rawTimes.Dequeue();}
  for(int bit=0;bit<=20;bit++){if(!ps5Refresh&&((p.Bits^ps5Previous)&(1u<<bit))==0)continue;int value=(p.Bits&(1u<<bit))!=0?1:0;inputMap.ObserveRaw(1,bit,value);controllerNavigation.Observe(inputMap,1,bit,value);}
  ps5Previous=p.Bits;ps5Refresh=false;
  if(learning&&learnReady&&now>=armAfter){int source=DualSenseButtonMap.SinglePress(pressed);if(source>=0)Capture("button:"+source);}
  // Axis signs supplied to PSP match the existing bridge (up is negative).
  gameAxes[0]=Ps5Axis(p.LX);gameAxes[1]=Ps5Axis(-p.LY);gameAxes[2]=Ps5Axis(p.RX);gameAxes[3]=Ps5Axis(-p.RY);
  NavigateController(now);UpdateGameButtons();
 }
 static short Ps5Axis(float value)=>Math.Abs(value)<=.10f?(short)0:(short)(Math.Sign(value)*(Math.Abs(value)-.10f)/.90f*32767);
 static string Ps5Source(string token){int i;if(token!=null&&token.StartsWith("button:")&&int.TryParse(token.Substring(7),out i)&&i>=0&&i<DualSenseButtonMap.Names.Length)return DualSenseButtonMap.Names[i];return "Unassigned";}
}
