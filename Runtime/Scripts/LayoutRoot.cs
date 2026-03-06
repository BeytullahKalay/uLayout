/*
    Copyright (c) 2026 Alex Howe

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.
*/

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poke.UI
{
    [
        ExecuteAlways,
        RequireComponent(typeof(RectTransform))
    ]
    public class LayoutRoot : MonoBehaviour
    {
        [SerializeField] private bool m_log;
        
        private readonly SortedBucket<Layout, int, Layout> _layouts = new (l => l, l => l.GetInstanceID());
        private readonly Stack<Layout> _reverse = new ();
        private bool _dirty;

        private void OnEnable() {
            _dirty = true;
        }

        private void Start() {
            UpdateLayout();
        }

        public void SetDirty() {
            _dirty = true;
        }

        public void Update() {
            foreach(Layout layout in _layouts) {
                layout.Tick();
            }
        }

        public void LateUpdate() {
            if(_dirty) {
                UpdateLayout();
            }
        }

        private void Log(object msg) {
            if(m_log) Debug.Log($"[{Time.frameCount}] <color=white>[Root]: {msg}</color>");
        }
        
        private void UpdateLayout() {
            Log("STARTING LAYOUT REFRESH");
            
            _reverse.Clear();
                
            // fit sizing pass (0)
            Log("Fit Size Pass");
            foreach(Layout l in _layouts) {
                if(l.NeedsRefresh) {
                    l.ComputeFitSize();
                    _reverse.Push(l);
                }
            }

            // grow sizing pass (1)
            Log("Grow Size Pass (DFS)");
            if(_reverse.Count > 0) {
                // start the grow propagation from the top-level layouts
                int lowestDepth = Int32.MaxValue;
                foreach(Layout l in _reverse) {
                    lowestDepth = Mathf.Min(l.Depth, lowestDepth);
                }

                Log($"lowest depth: {lowestDepth}");

                foreach(Layout l in _reverse) {
                    if(l.Depth == lowestDepth) {
                        Log($"growing layout \"{l.name}\"");
                        l.GrowChildren(RectTransform.Axis.Horizontal);
                        l.GrowChildren(RectTransform.Axis.Vertical);
                    }
                }
            }
            
            
            // layout pass (2)
            Log("Layout Pass");
            foreach(Layout l in _reverse) {
                l.ComputeLayout();
            }
            
            Log($"Refreshed {_reverse.Count} layouts");
            
            _dirty = false;
        }

        public void RegisterLayout(Layout layout) {
            Log($"Registered \"{layout.name}\" at depth [{layout.Depth}]");
            _layouts.Add(layout);
            SetDirty();
        }

        public void UnregisterLayout(Layout layout) {
            if(_layouts.Remove(layout)) {
                SetDirty();
                Log($"Removed \"{layout.name}\"");
            }
            else {
                Debug.LogError($"[Root]: Failed to remove \"{layout.name}\" (not found)");
            }
        }
    }
}