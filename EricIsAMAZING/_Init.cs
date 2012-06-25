﻿#region USINGZ

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
        [DebuggerStepThrough]
        public static void WriteLine(object o)
        {
#if DEBUG
            Debug.WriteLine(o);
#else
            Console.WriteLine(o);
#endif
        }

        [DebuggerStepThrough]
        public static void WriteLine(string format, params object[] args)
        {
#if DEBUG
            Debug.WriteLine(format, args);
#else
            Console.WriteLine(format, args);
#endif
        }
    }

    public static class ROS
    {
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
        public static int WallDuration = 100;

        public static RosOutAppender rosoutappender;
        public static NodeHandle GlobalNodeHandle;
        public static Timer internal_queue_thread;
        public static object shutting_down_mutex = new object();
        private static bool dictinit;
        private static Dictionary<string, Type> typedict = new Dictionary<string, Type>();

        public static Time GetTime()
        {
            TimeSpan timestamp = DateTime.Now.Subtract(new DateTime(1970, 1, 1, 0, 0, 0));
            uint seconds = (((uint) Math.Floor(timestamp.TotalSeconds) & 0xFFFFFFFF));
            Time stamp = new Time(new TimeData(seconds, (((uint) Math.Floor((timestamp.TotalSeconds - seconds)) << 32) & 0xFFFFFFFF)));
            return stamp;
        }

        public static IRosMessage MakeMessage(MsgTypes type)
        {
            return IRosMessage.generate(type);
        }

        public static IRosMessage MakeMessage(MsgTypes type, byte[] data)
        {
            IRosMessage msg = IRosMessage.generate(type).Deserialize(data);
            return msg;
        }

        public static G MakeAndDowncast<T, G>(params Type[] types)
        {
            if (typeof (T).IsGenericTypeDefinition)
                return (G) Activator.CreateInstance(typeof (T).MakeGenericType(types));
            return (G) Activator.CreateInstance(typeof (T));
        }

        public static void FREAKTHEFUCKOUT()
        {
            throw new Exception("ROS IS FREAKING THE FUCK OUT!");
        }

        [DebuggerStepThrough]
        public static void Info(object o)
        {
            if (initialized)
                rosoutappender.Append((string)o, RosOutAppender.ROSOUT_LEVEL.INFO);
        }

        [DebuggerStepThrough]
        public static void Info(string format, params object[] args)
        {
            Info((object)string.Format(format, args));
        }

        [DebuggerStepThrough]
        public static void Debug(object o)
        {
            if (initialized)
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
            if (initialized)
                rosoutappender.Append((string)o, RosOutAppender.ROSOUT_LEVEL.ERROR);
        }

        [DebuggerStepThrough]
        public static void Error(string format, params object[] args)
        {
            Error((object)string.Format(format, args));
        }

        public static void Init(string[] args, string name, int options = 0)
        {
            Console.WriteLine("INIT!");
            IDictionary remapping = new Hashtable();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Contains(":="))
                {
                    string[] chunks = args[i].Split(':');
                    chunks[1].TrimStart('=');
                    chunks[0].Trim();
                    chunks[1].Trim();
                    remapping.Add(chunks[0], chunks[1]);
                }
            }
            if (!string.IsNullOrEmpty(ROS.ROS_MASTER_URI))
            {
                remapping.Add("__master", ROS.ROS_MASTER_URI);
            }
            if (!string.IsNullOrEmpty(ROS.ROS_HOSTNAME))
            {
                remapping.Add("__hostname", ROS.ROS_HOSTNAME);
            }
            if (!string.IsNullOrEmpty(ROS.ROS_IP))
            {
                remapping.Add("__ip", ROS.ROS_IP);
            }

            Init(remapping, name, options);
        }

        public static void Init(IDictionary remapping_args, string name, int options = 0)
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

        public static void spinOnce()
        {
            GlobalCallbackQueue.callAvailable(WallDuration);
        }

        public static void spin()
        {
            spin(new SingleThreadSpinner());
        }

        public static void spin(Spinner spinner)
        {
            spinner.spin();
        }

        public static void checkForShutdown()
        {
            if (shutdown_requested && !shutting_down)
            {
                lock (shutting_down_mutex)
                {
                    shutdown();
                }
                shutdown_requested = false;
            }
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
                timer_manager.StartTimer(ref internal_queue_thread, internalCallbackQueueThreadFunc, 100,
                                         Timeout.Infinite);
                GlobalCallbackQueue.Enable();
            }
        }

        public static void internalCallbackQueueThreadFunc(object nothing)
        {
            GlobalCallbackQueue.callAvailable(100);
            if (!ok)
                timer_manager.RemoveTimer(ref internal_queue_thread);
            else
                timer_manager.StartTimer(ref internal_queue_thread, internalCallbackQueueThreadFunc, 100,
                                         Timeout.Infinite);
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
                else
                    shutting_down = true;

                EDB.WriteLine("We're going down down....");

                GlobalCallbackQueue.Disable();
                GlobalCallbackQueue.Clear();
                internal_queue_thread.Dispose();

                if (started)
                {
                    TopicManager.Instance.shutdown();
                    ServiceManager.Instance.shutdown();
                    PollManager.Instance.shutdown();
                    XmlRpcManager.Instance.shutdown();
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