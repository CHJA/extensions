﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.AspNet.MemoryCache
{
    internal class CacheEntry
    {
        private static readonly Action<object> ExpirationCallback = TriggerExpired;

        private readonly Action<CacheEntry> _notifyCacheOfExpiration;
        
        internal CacheEntry(CacheAddContext context, object value, Action<CacheEntry> notifyCacheOfExpiration)
        {
            Context = context;
            Value = value;
            _notifyCacheOfExpiration = notifyCacheOfExpiration;
            LastAccessed = context.CreationTime;
        }

        internal CacheAddContext Context { get; private set; }

        private bool IsExpired { get; set; }

        internal EvictionReason EvictionReason { get; private set; }

        internal object Value { get; private set; }

        internal IList<IDisposable> TriggerRegistrations { get; set; }

        internal DateTime LastAccessed { get; set; }

        internal bool CheckExpired(DateTime now)
        {
            return IsExpired || CheckForExpiredTime(now) || CheckForExpiredTriggers();
        }

        internal void SetExpired(EvictionReason reason)
        {
            IsExpired = true;
            if (EvictionReason == EvictionReason.None)
            {
                EvictionReason = reason;
            }
        }

        private bool CheckForExpiredTime(DateTime now)
        {
            if (Context.AbsoluteExpiration.HasValue && Context.AbsoluteExpiration.Value <= now)
            {
                SetExpired(EvictionReason.Expired);
                return true;
            }

            if (Context.SlidingExpiration.HasValue
                && (now - LastAccessed) >= Context.SlidingExpiration)
            {
                SetExpired(EvictionReason.Expired);
                return true;
            }

            return false;
        }

        internal bool CheckForExpiredTriggers()
        {
            var triggers = Context.Triggers;
            if (triggers != null)
            {
                for (int i = 0; i < triggers.Count; i++)
                {
                    var trigger = triggers[i];
                    if (trigger.IsExpired)
                    {
                        SetExpired(EvictionReason.Triggered);
                        return true;
                    }
                }
            }
            return false;
        }

        // TODO: There's a possible race between AttachTriggers and DetachTriggers if a trigger fires almost immidiately.
        // This may result in some registrations not getting disposed.
        internal void AttachTriggers()
        {
            var triggers = Context.Triggers;
            if (triggers != null)
            {
                for (int i = 0; i < triggers.Count; i++)
                {
                    var trigger = triggers[i];
                    if (trigger.ActiveExpirationCallbacks)
                    {
                        if (TriggerRegistrations == null)
                        {
                            TriggerRegistrations = new List<IDisposable>(1);
                        }
                        var registration = trigger.RegisterExpirationCallback(ExpirationCallback, this);
                        TriggerRegistrations.Add(registration);
                    }
                }
            }
        }

        private static void TriggerExpired(object obj)
        {
            var entry = (CacheEntry)obj;
            entry.SetExpired(EvictionReason.Triggered);
            entry._notifyCacheOfExpiration(entry);
        }

        internal void DetatchTriggers()
        {
            var registrations = TriggerRegistrations;
            if (registrations != null)
            {
                TriggerRegistrations = null;
                for (int i = 0; i < registrations.Count; i++)
                {
                    var registration = registrations[i];
                    registration.Dispose();
                }
            }
        }

        // TODO: Ensure a thread safe way to prevent these from being invoked more than once;
        internal void InvokeEvictionCallbacks()
        {
            var callbacks = Context.PostEvictionCallbacks;
            Context.PostEvictionCallbacks = null;
            if (callbacks != null)
            {
                for (int i = 0; i < callbacks.Count; i++)
                {
                    var callbackPair = callbacks[i];
                    var callback = callbackPair.Item1;
                    var state = callbackPair.Item2;

                    try
                    {
                        callback(Context.Key, Value, EvictionReason, state);
                    }
                    catch (Exception)
                    {
                        // This will often be invoked on a background thread, don't let throw.
                        // TODO: LOG
                    }
                }
            }
        }
    }
}