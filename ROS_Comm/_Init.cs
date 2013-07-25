﻿#region Using

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using Messages;
using Messages.std_msgs;
using XmlRpc_Wrapper;
using m = Messages.std_msgs;
using gm = Messages.geometry_msgs;
using nm = Messages.nav_msgs;

#endregion

namespace Ros_CSharp
{
    public static class EDB
    {
        public delegate void otheroutput(object o);
        public static event otheroutput OtherOutput;
        

        private static void _writeline(object o)
        {
#if DEBUG
            if (OtherOutput != null)
                OtherOutput(o);
            Debug.WriteLine(o);
#else
            Console.WriteLine(o);
#endif
        }

        //[DebuggerStepThrough]
        public static void WriteLine(object o)
        {
            _writeline(o);
        }

        //[DebuggerStepThrough]
        public static void WriteLine(string format, params object[] args)
        {
#if DEBUG
            if (args != null && args.Length > 0)
                _writeline(string.Format(format, args));
            else
                _writeline(format);
#else
            if (args != null && args.Length > 0)
                Console.WriteLine(string.Format(format, args));
            else
                Console.WriteLine(format);
#endif
        }
    }

    public static class ROS
    {
        public static System.UInt64 getPID()
        {
           //ProcessThreadCollection ptc = Process.GetCurrentProcess().Threads;
            //if (Thread.CurrentThread.ManagedThreadId >= ptc.Count)
                return (System.UInt64)Thread.CurrentThread.ManagedThreadId;
            //return (System.UInt64)ptc[Thread.CurrentThread.ManagedThreadId].Id;
        }


        public static TimerManager timer_manager = new TimerManager();

        public static CallbackQueue GlobalCallbackQueue;
        public static bool initialized, started, atexit_registered, ok, shutting_down, shutdown_requested;
        public static int init_options;
        public static string ROS_MASTER_URI;
        public static string ROS_HOSTNAME;
        public static string ROS_IP;
        public static object start_mutex = new object();

        /// <summary>
        ///   general global sleep time in miliseconds
        /// </summary>
        public static int WallDuration = 20;

        public static RosOutAppender rosoutappender;
        public static NodeHandle GlobalNodeHandle;
        public static object shutting_down_mutex = new object();
        private static bool dictinit;

        private static long frequency = Stopwatch.Frequency;
        private static long nanosecPerTick = (1000L * 1000L * 1000L) / frequency;
        private static Dictionary<string, Type> typedict = new Dictionary<string, Type>();

        public static Time GetTime(DateTime time)
        {
            return GetTime(time.Subtract(new DateTime(1970, 1, 1, 0, 0, 0)));
        }
        public static Time GetTime(TimeSpan timestamp)
        {
            uint seconds = (((uint)Math.Floor(timestamp.TotalSeconds) & 0xFFFFFFFF));
            uint nanoseconds = ((uint)Math.Floor(((double)(timestamp.TotalSeconds - seconds) * 1000000000)));
            Time stamp = new Time(new TimeData(seconds, nanoseconds));
            return stamp;
        }
        public static Time GetTime()
        {
            return GetTime(DateTime.Now);
        }

        public static IRosMessage MakeMessage(MsgTypes type)
        {
            return IRosMessage.generate(type);
        }

        public static IRosMessage MakeMessage(MsgTypes type, byte[] data)
        {
            IRosMessage msg = IRosMessage.generate(type);
            msg.Deserialize(data);
            return msg;
        }

        //new Subscriber<type>()
        //MakeAndDownCast<Subscriber<>, IRosMessage>(typeof(type));
        public static G MakeAndDowncast<T, G>(params Type[] types)
        {
            if (typeof (T).IsGenericTypeDefinition)
                return (G) Activator.CreateInstance(typeof (T).MakeGenericType(types));
            return (G) Activator.CreateInstance(typeof (T));
        }

        public static void FREAKOUT()
        {
            throw new Exception("ROS IS FREAKING OUT!");
        }

        [DebuggerStepThrough]
        public static void Info(object o)
        {
            if (initialized && rosoutappender != null)
                rosoutappender.Append((string)o, RosOutAppender.ROSOUT_LEVEL.INFO);
        }

        [DebuggerStepThrough]
        public static void Info(string format, params object[] args)
        {
            string s = string.Format(format, args);
            Console.WriteLine("[Info] " + s);
            Info((object)s);
        }

        [DebuggerStepThrough]
        public static void Debug(object o)
        {
            if (initialized && rosoutappender != null)
                rosoutappender.Append((string)o, RosOutAppender.ROSOUT_LEVEL.DEBUG);
        }

        [DebuggerStepThrough]
        public static void Debug(string format, params object[] args)
        {
            Debug((object)string.Format(format, args));
        }

        [DebuggerStepThrough]
        public static void Error(object o)
        {
            if (initialized && rosoutappender != null)
                rosoutappender.Append((string)o, RosOutAppender.ROSOUT_LEVEL.ERROR);
        }

        [DebuggerStepThrough]
        public static void Error(string format, params object[] args)
        {
            string s = string.Format(format, args);
            Console.WriteLine("[Error] " + s);
            Error((object)string.Format(format, args));
        }

        public static void Init(string[] args, string name)
        {
            Init(args, name, 0);
        }

        public static void Init(string[] args, string name, int options)
        {
            // ROS_MASTER_URI/ROS_HOSTNAME definition precedence:
            // 1. explicitely set by program
            // 2. passed in as remap argument
            // 3. environment variable

            IDictionary remapping = new Hashtable();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Contains(":="))
                {
                    string[] chunks = args[i].Split(':');
                    chunks[1] = chunks[1].TrimStart('=').Trim();
                    chunks[0] = chunks[0].Trim();
                    remapping.Add(chunks[0], chunks[1]);
                    switch (chunks[0])
                    {
                        //if already defined, then it was defined by the program, so leave it
                        case "__master": if (string.IsNullOrEmpty(ROS.ROS_MASTER_URI)) ROS.ROS_MASTER_URI = chunks[1].Trim(); break;
                        case "__hostname": if (string.IsNullOrEmpty(ROS.ROS_HOSTNAME)) ROS.ROS_HOSTNAME = chunks[1].Trim(); break;
                    }
                }
            }

            //If ROS.ROS_MASTER_URI was not explicitely set by the program calling Init, and was not passed in as a remapping argument, then try to find it in ENV.
            if (string.IsNullOrEmpty(ROS.ROS_MASTER_URI))
            {
                IDictionary _vars;

                //check user env first, then machine if user doesn't have uri defined.
                if ((_vars = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.User)).Contains("ROS_MASTER_URI")
                    || (_vars = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Machine)).Contains("ROS_MASTER_URI"))
                    ROS.ROS_MASTER_URI = (string)_vars["ROS_MASTER_URI"];
            }

            //if defined NOW, then add to remapping, or replace remapping (in the case it was explicitly set by program AND was passed as remapping arg)
            if (!string.IsNullOrEmpty(ROS.ROS_MASTER_URI))
            {
                if (remapping.Contains("__master"))
                    remapping["__master"] = ROS.ROS_MASTER_URI;
                else
                    remapping.Add("__master", ROS.ROS_MASTER_URI);
            }
            else
                //this is fatal
                throw new Exception("Unknown ROS_MASTER_URI\n" + @"ROS_MASTER_URI needs to be defined for your program to function.
Either:
    set an environment variable called ROS_MASTER_URI,
    pass a __master remapping argument to your program, 
    or set the URI explicitely in your program before calling Init.");

            if (!string.IsNullOrEmpty(ROS.ROS_HOSTNAME))
            {
                if (remapping.Contains("__hostname"))
                    remapping["__hostname"] = ROS.ROS_MASTER_URI;
                else
                    remapping.Add("__hostname", ROS.ROS_MASTER_URI);
            }

            if (!string.IsNullOrEmpty(ROS.ROS_IP))
            {
                if (remapping.Contains("__ip"))
                    remapping["__ip"] = ROS.ROS_MASTER_URI;
                else
                    remapping.Add("__ip", ROS.ROS_MASTER_URI);
            }

            Init(remapping, name, options);
        }

        public static void Init(IDictionary remapping_args, string name)
        {
             Init(remapping_args, name, 0);
        }
        internal static List<CallbackQueue> callbax = new List<CallbackQueue>();
        public static void Init(IDictionary remapping_args, string name, int options)
        {
            if (!atexit_registered)
            {
                atexit_registered = true;
                Process.GetCurrentProcess().EnableRaisingEvents = true;
                Process.GetCurrentProcess().Exited += (o, args) => shutdown();
            }

            if (GlobalCallbackQueue == null)
            {
                GlobalCallbackQueue = new CallbackQueue();
                callbax.Add(GlobalCallbackQueue);
            }

            if (!initialized)
            {
                init_options = options;
                ok = true;
                network.init(remapping_args);
                master.init(remapping_args);
                this_node.Init(name, remapping_args, options);
                Param.init(remapping_args);
                initialized = true;
                GlobalNodeHandle = new NodeHandle(this_node.Namespace, remapping_args);
            }
        }

        public static void checkForShutdown()
        {
            lock (shutting_down_mutex)
            {
                if (!shutdown_requested || shutting_down)
                    return;
            }
            shutdown();
            shutdown_requested = false;
        }

        public static void shutdownCallback(IntPtr p, IntPtr r)
        {
            XmlRpcValue parms = XmlRpcValue.LookUp(p);
            int num_params = 0;
            if (parms.Type == TypeEnum.TypeArray)
                num_params = parms.Size;
            if (num_params > 1)
            {
                string reason = parms[1].Get<string>();
                EDB.WriteLine("Shutdown request received.");
                EDB.WriteLine("Reason given for shutdown: [" + reason + "]");
                requestShutdown();
            }
            XmlRpcManager.Instance.responseInt(1, "", 0)(r);
        }

        public static void waitForShutdown()
        {
            while (ok)
            {
                Thread.Sleep(WallDuration);
            }
        }

        public static void requestShutdown()
        {
            shutdown_requested = true;
        }

        public static void start()
        {
            lock (start_mutex)
            {
                if (started) return;
                shutdown_requested = false;
                shutting_down = false;
                started = true;
                ok = true;
                PollManager.Instance.addPollThreadListener(checkForShutdown);
                XmlRpcManager.Instance.bind("shutdown", shutdownCallback);
                //initInternalTimerManager();
                TopicManager.Instance.Start();
                try
                {
                    ServiceManager.Instance.Start();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
                ConnectionManager.Instance.Start();
                PollManager.Instance.Start();
                XmlRpcManager.Instance.Start();

                rosoutappender = new RosOutAppender();

                //Time.Init();
                GlobalCallbackQueue.Enable();
            }
        }

        public static bool isStarted()
        {
            return false;
        }


        public static void shutdown()
        {
            lock (shutting_down_mutex)
            {
                if (shutting_down)
                    return;
                shutting_down = true;
                ok = false;

                EDB.WriteLine("We're going down down....");

                GlobalCallbackQueue.Disable();
                GlobalCallbackQueue.Clear();

                if (started)
                {
                    TopicManager.Instance.shutdown();
                    ServiceManager.Instance.shutdown();
                    PollManager.Instance.shutdown();
                    XmlRpcManager.Instance.shutdown();
                    rosoutappender.shutdown();
                }

                started = false;
                ok = false;
            }
        }

        public static void removeROSArgs(string[] args, out string[] argsout)
        {
            List<string> argssss = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (!arg.Contains(":="))
                    argssss.Add(arg);
            }
            argsout = argssss.ToArray();
        }

        public static Type GetDataType(string name)
        {
            if (!dictinit)
            {
                dictinit = true;
                foreach (
                    Assembly a in AppDomain.CurrentDomain.GetAssemblies().Union(new[] {Assembly.GetExecutingAssembly()})
                    )
                {
                    foreach (Type t in a.GetTypes())
                    {
                        if (!typedict.ContainsKey(t.ToString()))
                        {
                            typedict.Add(t.ToString(), t);
                        }
                    }
                }
            }
            return typedict[name];
        }
    }

    public enum InitOption
    {
        NosigintHandler = 1 << 0,
        AnonymousName = 1 << 1,
        NoRousout = 1 << 2
    }
}