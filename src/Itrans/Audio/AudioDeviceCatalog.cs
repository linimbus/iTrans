using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Itrans.Audio;

public static class AudioDeviceCatalog
{
    public static IReadOnlyList<AudioApplication> GetApplications()
    {
        var result = new Dictionary<uint, string>();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            using var manager = device.AudioSessionManager;
            manager.RefreshSessions();

            var sessions = manager.Sessions;
            for (var index = 0; index < sessions.Count; index++)
            {
                var session = sessions[index];
                try
                {
                    var processId = session.GetProcessID;
                    if (processId == 0 || processId == Environment.ProcessId) continue;
                    if (session.IsSystemSoundsSession) continue;

                    var state = session.State;
                    if (state != AudioSessionState.AudioSessionStateActive
                        && state != AudioSessionState.AudioSessionStateInactive)
                    {
                        continue;
                    }

                    var name = "";
                    try
                    {
                        name = session.DisplayName;
                    }
                    catch
                    {
                    }

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        try
                        {
                            using var process = Process.GetProcessById((int)processId);
                            name = process.ProcessName;
                        }
                        catch
                        {
                            name = "PID " + processId;
                        }
                    }

                    if (!result.ContainsKey(processId)) result[processId] = name;
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        return result
            .Select(pair => new AudioApplication(pair.Key, pair.Value))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<AudioDevice> GetMicrophones(out string? defaultDeviceId)
    {
        defaultDeviceId = null;
        var devices = new List<AudioDevice>();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            try
            {
                using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                defaultDeviceId = defaultDevice.ID;
            }
            catch
            {
            }

            using var collection = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var device in collection)
            {
                try
                {
                    devices.Add(new AudioDevice(device.ID, device.FriendlyName));
                }
                catch
                {
                }
                finally
                {
                    device.Dispose();
                }
            }
        }
        catch
        {
        }

        return devices;
    }

    public static MMDevice? GetMicrophone(string deviceId)
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(deviceId);
            enumerator.Dispose();
            return device;
        }
        catch
        {
            return null;
        }
    }
}
