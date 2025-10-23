﻿using SiraUtil.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Zenject;

/// <summary>
/// Unity main thread dispatcher class
/// Intended to replace the original games thread dispatcher, which was removed in 1.34.2
/// 
/// Written by Arimodu for AwayPlayer in November 2023
/// 
/// This class is public domain
/// </summary>


#pragma warning disable CS0649 // Value is never assigend to - We have zenject
public class UnityMainThreadDispatcher : ITickable
{
    [Inject]
    private readonly SiraLog log;

    private readonly Queue<Action> TaskQueue = new Queue<Action>();
    private readonly List<EnqueuedTask> DelayedTasks = new List<EnqueuedTask>();
    private readonly List<EnqueuedTask> PausedTasks = new List<EnqueuedTask>();
    private readonly List<Guid> FinishedTasks = new List<Guid>();

    public void Tick()
    {
        lock (TaskQueue)
        {
            while (TaskQueue.Count > 0)
            {
                Action action = TaskQueue.Dequeue();
                action.Invoke();
            }
        }

        lock (DelayedTasks)
        {
            var currentTime = (int)(Time.time * 1000);

            List<EnqueuedTask> actionsToRemove = new List<EnqueuedTask>();

            for (int i = 0; i < DelayedTasks.Count; i++)
            {
                var action = DelayedTasks[i];
                if (currentTime >= action.Timeout)
                {
                    //log.Info("Invoking target");
                    action.Invoke();
                    actionsToRemove.Add(action);
                    //log.Info("Target invoked");
                    continue;
                }

                if (action.Callback != null && currentTime >= action.NextCallback)
                {
                    //log.Info("Calling back from main thread");
                    action.InvokeCallback();
                    DelayedTasks[i].IncrementCallback();
                    //log.Info("Callback finished");
                }
            }

            foreach (var actionToRemove in actionsToRemove)
            {
                DelayedTasks.Remove(actionToRemove);
                FinishedTasks.Add(actionToRemove.Id);
            }
        }
    }

    public void Enqueue(Action action)
    {
        lock (TaskQueue)
        {
            TaskQueue.Enqueue(action);
        }
    }

    public void Enqueue(Func<Task> asyncAction)
    {
        async Task WrapperAsync()
        {
            await asyncAction.Invoke();
        }

        Enqueue(() => WrapperAsync().ConfigureAwait(true));
    }

    public Guid EnqueueWithDelay(Action target, int delayMilliseconds)
    {
        return EnqueueWithDelay(target, delayMilliseconds, null);
    }

    public Guid EnqueueWithDelay(Action target, int delayMilliseconds, Action<int> callback)
    {
        return EnqueueWithDelay(target, delayMilliseconds, callback, 1000); // Default callback interval of 1 second
    }

    public Guid EnqueueWithDelay(Action target, int delayMilliseconds, Action<int> callback, int callbackInterval)
    {
        int invokeAtMs = ((int)Time.time * 1000) + delayMilliseconds;
        int nextCallback = ((int)Time.time * 1000) + callbackInterval;

        var task = new EnqueuedTask(target, invokeAtMs, callback, nextCallback, callbackInterval);

        lock (DelayedTasks)
        {
            DelayedTasks.Add(task);
        }

        log.Info($"New deffered job enqueued! It will run in {delayMilliseconds} ms" + (callback == null ? "." : $" and callback every {callbackInterval} ms."));
        return task.Id;
    }

    public Guid EnqueueWithDelay(Func<Task> asyncTarget, int delayMilliseconds)
    {
        return EnqueueWithDelay(asyncTarget, delayMilliseconds, null);
    }

    public Guid EnqueueWithDelay(Func<Task> asyncTarget, int delayMilliseconds, Action<int> callback)
    {
        return EnqueueWithDelay(asyncTarget, delayMilliseconds, callback, 1000); // Default callback interval of 1 second
    }

    public Guid EnqueueWithDelay(Func<Task> asyncTarget, int delayMilliseconds, Action<int> callback, int callbackInterval)
    {
        async Task WrapperAsync()
        {
            await asyncTarget.Invoke();
        }

        return EnqueueWithDelay(() => WrapperAsync().ConfigureAwait(true), delayMilliseconds, callback, callbackInterval);
    }

    public void CancelDelayedTask(Guid id)
    {
        lock (DelayedTasks)
        {
            if (DelayedTasks.Any((task) => task.Id == id))
            {
                DelayedTasks.RemoveAt(DelayedTasks.FindIndex(task => task.Id == id));
                log.Info($"Successfully removed job ID: {id}");
            }
            else log.Warn($"Failed to remove job \nID: {id} \nReason: {(FinishedTasks.Contains(id) ? "Job already completed" : "No such job exists")}");
        }
    }

    public void TryCancelDelayedTask(Guid id)
    {
        if (DelayedTasks.Any((task) => task.Id == id))
        {
            DelayedTasks.RemoveAt(DelayedTasks.FindIndex(task => task.Id == id));
            log.Info($"Successfully removed job ID: {id}");
        }
    }

    public void PauseTask(Guid id)
    {
        lock (DelayedTasks)
        {
            if (DelayedTasks.Any((task) => task.Id == id))
            {
                var index = DelayedTasks.FindIndex(x => x.Id == id);
                var task = DelayedTasks[index];
                DelayedTasks.RemoveAt(index);
                task.Pause();
                PausedTasks.Add(task);
            }
            else log.Warn($"Failed to pause job \nID: {id} \nReason: {(FinishedTasks.Contains(id) ? "Job already completed" : "No such job exists")}");
        }
    }

    public void ResumeTask(Guid id)
    {
        lock (PausedTasks)
        {
            if (PausedTasks.Any((task) => task.Id == id))
            {
                var index = PausedTasks.FindIndex(x => x.Id == id);
                var task = PausedTasks[index];
                PausedTasks.RemoveAt(index);
                task.Resume();
                DelayedTasks.Add(task);
            }
            else log.Warn($"Failed to resume job \nID: {id} \nReason: {(FinishedTasks.Contains(id) ? "Job already completed" : "No such job exists")}");
        }
    }

    private class EnqueuedTask
    {
        public Guid Id { get; private set; }
        public Action Target { get; private set; }
        public Action<int> Callback { get; private set; }
        public int Timeout { get; private set; }
        public int CallbackInterval { get; private set; }
        public int NextCallback { get; private set; }

        private int PausedAt;

        public EnqueuedTask(Action target, int timeout, Action<int> callback, int nextCallback, int callbackInterval)
        {
            Id = Guid.NewGuid();
            Target = target;
            Callback = callback;
            CallbackInterval = callbackInterval;
            Timeout = timeout;
            NextCallback = nextCallback;
        }

        public void IncrementCallback() => NextCallback += CallbackInterval;
        public void Invoke() => Target.Invoke();
        public void InvokeCallback() => Callback.Invoke((int)((Timeout - (Time.time * 1000))/1000));
        public void Pause() => PausedAt = (int)(Time.time * 1000);
        public void Resume() => Timeout += (int)(Time.time * 1000) - PausedAt;
    }
}
