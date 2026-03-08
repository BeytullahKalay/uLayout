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
using UnityEngine.UI;

namespace Poke.UI
{
    public class Layout : LayoutItem, IComparable<Layout>, ILayoutGroup
    {
        /* THINGS THAT CAN CAUSE A LAYOUT UPDATE
            - non-grow child RectTransform changes size
            - number of children change
            - child is enabled/disabled
            - this container changes
        */
        
        [Header("Layout")]
        [SerializeField] private Margins            m_padding;
        [SerializeField] private LayoutDirection    m_direction;
        [SerializeField] private Justification      m_justifyContent;
        [SerializeField] private Alignment          m_alignContent;
        [SerializeField] private float              m_innerSpacing;
        [SerializeField] private bool               m_ignoreChildScale;

        public int ChildCount =>            _children?.Count ?? 0;
        public Vector2Int GrowChildCount => _growChildCount;
        
        private readonly List<ChildInfo>    _children = new();
        private Vector2                     _contentSize;
        private int                         _depth;
        private Vector2Int                  _growChildCount;
        private int                         _ignoreCount;
        private Vector2                     _lastSize;
        private readonly Vector3[]          _rectCorners = new Vector3[4];

        #region TypeDef
        public enum Justification
        {
            Start,
            Center,
            End,
            SpaceBetween
        }
        
        public enum Alignment
        {
            Start,
            Center,
            End
        }
        
        public enum LayoutDirection
        {
            Row,
            Column,
            RowReverse,
            ColumnReverse
        }

        private class ChildInfo
        {
            public int index;
            public RectTransform rect;
            public LayoutItem li;
            public bool isLayout;
            public Vector2 size;
            public bool enabled;
            public bool ignoreLayout;
        }
        #endregion
        
        #region Layout MonoBehavior
        protected override void OnEnable() {
            base.OnEnable();
            Log("enable");
            
            RefreshChildCache();
        }

        public override void Update() {
            base.Update();
            
            bool layoutChanged = _dirty;
            bool needsCacheRefresh = false;
            
            // check for changes in children
            foreach(ChildInfo c in _children) {
                if(!c.rect) {
                    layoutChanged = true;
                    needsCacheRefresh = true;
                    continue;
                }
                
                // check if child index has changed
                if(c.rect.GetSiblingIndex() != c.index) {
                    layoutChanged = true;
                    needsCacheRefresh = true;
                }
                
                // check if item was disabled this frame
                if(c.rect.gameObject.activeInHierarchy != c.enabled) {
                    c.enabled = c.rect.gameObject.activeInHierarchy;
                    layoutChanged = true;
                }


                if(c.li) {
                    // check if ignore layout toggled this frame
                    if(c.li.IgnoreLayout != c.ignoreLayout) {
                        c.ignoreLayout = c.li.IgnoreLayout;
                        layoutChanged = true;
                    }
                }
                else {
                    _tracker.Add(
                        this,
                        c.rect,
                        DrivenTransformProperties.AnchoredPosition | DrivenTransformProperties.Pivot
                            | DrivenTransformProperties.Anchors
                    );
                }
            }
            
            // check if the container changed this frame
            if(!Mathf.Approximately(_lastSize.x, _rect.rect.size.x) || !Mathf.Approximately(_lastSize.y, _rect.rect.size.y)) {
                layoutChanged = true;
            }
            // check if any children were added/removed this frame
            if(transform.childCount != _children.Count) {
                layoutChanged = true;
                needsCacheRefresh = true;
            }
            
            if(layoutChanged) {
                LayoutRebuilder.MarkLayoutForRebuild(_rect);
                if(needsCacheRefresh)
                    RefreshChildCache();

                _dirty = false;
            }

            _lastSize = _rect.rect.size;
        }
        
        private void OnDrawGizmosSelected() {
            _rect.GetWorldCorners(_rectCorners);

            Matrix4x4 ltw = _rect.localToWorldMatrix;
            
            foreach(Vector3 v in _rectCorners) {
                LayoutUtil.DrawCenteredDebugBox(v, 0.15f, 0.15f, Color.red);
            }

            Rect r = new Rect(_rectCorners[0], _rectCorners[2] - _rectCorners[0]);
            r.position += (Vector2)(ltw * new Vector2(m_padding.left, m_padding.bottom));
            r.size -= (Vector2)(ltw * new Vector2(m_padding.left + m_padding.right, m_padding.top + m_padding.bottom));
            
            LayoutUtil.DrawDebugBox(r, _rect.position.z, Color.green);
        }
        #endregion

        #region ILayoutGroup
        public override void CalculateLayoutInputHorizontal() {
            base.CalculateLayoutInputHorizontal();
            
            _growChildCount.x = 0;
            _ignoreCount = 0;
            
            if(_children.Count > 0) {
                // get number of disabled/ignore children
                foreach(ChildInfo c in _children) {
                    if(CheckIgnoreElem(c)) {
                        _ignoreCount++;
                    }
                    else {
                        c.size = c.size.SetX(c.rect.rect.size.x * (m_ignoreChildScale ? 1 : c.rect.localScale.x));
                    }
                }

                float primarySize = m_justifyContent == Justification.SpaceBetween ? 0 : m_innerSpacing * (_children.Count-_ignoreCount-1);
                float crossSize = 0;
                
                // calculate content size
                float maxCrossSize = 0;
                foreach(ChildInfo c in _children) {
                    // skip disabled/ignore items
                    if(CheckIgnoreElem(c))
                        continue;
                    
                    bool grow = false;
                    if(c.li) {
                        grow = c.li.SizeMode.x == SizingMode.Grow;
                        if(grow) {
                            _growChildCount.x++;
                        }
                    }
                    
                    switch(m_direction) {
                        case LayoutDirection.Row:
                        case LayoutDirection.RowReverse:
                            primarySize += grow ? 0 : c.size.x;
                            break;
                        case LayoutDirection.Column:
                        case LayoutDirection.ColumnReverse:
                            maxCrossSize = Mathf.Max(maxCrossSize, grow ? 0 : c.size.x);
                            break;
                    }
                }
                crossSize += maxCrossSize;

                // save content size for later
                switch(m_direction) {
                    case LayoutDirection.Row:
                    case LayoutDirection.RowReverse:
                        _contentSize.x = primarySize;
                        break;
                    case LayoutDirection.Column:
                    case LayoutDirection.ColumnReverse:
                        _contentSize.x = crossSize;
                        break;
                }
                
                // apply fit sizing X
                if(m_sizing.x == SizingMode.FitContent) {
                    switch(m_direction) {
                        case LayoutDirection.Row:
                        case LayoutDirection.RowReverse:
                            _rect.SetSizeWithCurrentAnchors(
                                RectTransform.Axis.Horizontal,
                                primarySize + m_padding.left + m_padding.right
                            );
                            break;
                        case LayoutDirection.Column:
                        case LayoutDirection.ColumnReverse:
                            _rect.SetSizeWithCurrentAnchors(
                                RectTransform.Axis.Horizontal,
                                crossSize + m_padding.left + m_padding.right
                            );
                            break;
                    }
                }
                
                Log($"calculated rect x size: {_rect.rect.size.x:f3}");
            }
            else {
                _contentSize = Vector2.zero;
            }
            
            Log($"content x size: {_contentSize.x:f3}");
        }
        
        public override void CalculateLayoutInputVertical() {
            base.CalculateLayoutInputVertical();
            
            _growChildCount.y = 0;
            
            if(_children.Count > 0) {
                foreach(ChildInfo c in _children) {
                    if(!CheckIgnoreElem(c)) {
                        c.size = c.size.SetY(c.rect.rect.size.y * (m_ignoreChildScale ? 1 : c.rect.localScale.y));
                    }
                }
                
                float primarySize = m_justifyContent == Justification.SpaceBetween ? 0 : m_innerSpacing * (_children.Count-_ignoreCount-1);
                float crossSize = 0;
                
                // calculate content size
                float maxCrossSize = 0;
                foreach(ChildInfo c in _children) {
                    // skip disabled/ignore items
                    if(CheckIgnoreElem(c))
                        continue;
                    
                    bool grow = false;
                    if(c.li) {
                        grow = c.li.SizeMode.y == SizingMode.Grow;
                        if(grow) {
                            _growChildCount.y++;
                        }
                    }
                    
                    switch(m_direction) {
                        case LayoutDirection.Row:
                        case LayoutDirection.RowReverse:
                            maxCrossSize = Mathf.Max(maxCrossSize, grow ? 0 : c.size.y);
                            break;
                        case LayoutDirection.Column:
                        case LayoutDirection.ColumnReverse:
                            primarySize += grow ? 0 : c.size.y;
                            break;
                    }
                }
                crossSize += maxCrossSize;

                // save content size for later
                switch(m_direction) {
                    case LayoutDirection.Row:
                    case LayoutDirection.RowReverse:
                        _contentSize.y = crossSize;
                        break;
                    case LayoutDirection.Column:
                    case LayoutDirection.ColumnReverse:
                        _contentSize.y = primarySize;
                        break;
                }
                
                // apply fit sizing X
                if(m_sizing.y == SizingMode.FitContent) {
                    switch(m_direction) {
                        case LayoutDirection.Row:
                        case LayoutDirection.RowReverse:
                            _rect.SetSizeWithCurrentAnchors(
                                RectTransform.Axis.Vertical,
                                crossSize + m_padding.top + m_padding.bottom
                            );
                            break;
                        case LayoutDirection.Column:
                        case LayoutDirection.ColumnReverse:
                            _rect.SetSizeWithCurrentAnchors(
                                RectTransform.Axis.Vertical,
                                primarySize + m_padding.top + m_padding.bottom
                            );
                            break;
                    }
                }
                
                Log($"calculated rect y size: {_rect.rect.size.y:f3}");
            }
            else {
                _contentSize = Vector2.zero;
            }
            
            Log($"content x size: {_contentSize.y:f3}");
        }

        public void SetLayoutHorizontal() {
            Log("SetLayoutHorizontal");

            float offset = 0;
            float leftover;
            float spacing = 0;
            int index = 0;
            switch(m_direction) {
                // ROW -> PRIMARY AXIS
                case LayoutDirection.Row:
                    switch(m_justifyContent) {
                        case Justification.Start:
                            offset += m_padding.left;
                            foreach(ChildInfo c in _children) {
                                // skip disabled/ignore items
                                if(CheckIgnoreElem(c))
                                    continue;
                                
                                SetAnchorX(c.rect, 0);

                                float pivot = c.size.x * c.rect.pivot.x;
                                offset += pivot;
                                c.rect.anchoredPosition = c.rect.anchoredPosition.SetX(offset);
                                offset += (c.size.x - pivot) + m_innerSpacing;
                            }
                            break;
                        case Justification.Center:
                            offset -= (_contentSize.x + m_padding.left + m_padding.right) / 2;
                            
                            foreach(ChildInfo c in _children) {
                                // skip disabled/ignore items
                                if(CheckIgnoreElem(c))
                                    continue;
                                
                                SetAnchorX(c.rect, 0.5f);
                            
                                float pivot = c.size.x * c.rect.pivot.x;
                                offset += pivot;
                                c.rect.anchoredPosition = c.rect.anchoredPosition.SetX(offset + m_padding.left);
                                offset += (c.size.x-pivot) + m_innerSpacing;
                            }
                            break;
                        case Justification.End:
                            offset -= m_padding.right + _contentSize.x;
                            
                            foreach(ChildInfo c in _children) {
                                // skip disabled/ignore items
                                if(CheckIgnoreElem(c))
                                    continue;
                                
                                SetAnchorX(c.rect, 1);

                                float pivot = c.size.x * c.rect.pivot.x;
                                offset += pivot;
                                c.rect.anchoredPosition = c.rect.anchoredPosition.SetX(offset);
                                offset += (c.size.x - pivot) + m_innerSpacing;
                            }
                            break;
                        case Justification.SpaceBetween:
                            offset += m_padding.left;
                            leftover = _rect.rect.size.x - _contentSize.x - m_padding.left - m_padding.right;
                            
                            if(_children.Count > 1)
                                spacing = leftover / (_children.Count-_ignoreCount-1);
                            
                            foreach(ChildInfo c in _children) {
                                // skip disabled/ignore items
                                if(CheckIgnoreElem(c))
                                    continue;
                                
                                SetAnchorX(c.rect, 0);
                            
                                if(index != 0) {
                                    offset += spacing;
                                }

                                float pivot = c.size.x * c.rect.pivot.x;
                                offset += pivot;
                                c.rect.anchoredPosition = c.rect.anchoredPosition.SetX(offset);
                                offset += c.size.x - pivot;
                                index++;
                            }
                            break;
                    }
                    break;
                // ROW-REVERSE -> PRIMARY AXIS
                case LayoutDirection.RowReverse:
                    switch(m_justifyContent) {
                        case Justification.Start:
                            offset += m_padding.left + _contentSize.x;
                            
                            foreach(ChildInfo c in _children) {
                                // skip disabled/ignore items
                                if(CheckIgnoreElem(c))
                                    continue;
                                
                                SetAnchorX(c.rect, 0);

                                float pivot = c.size.x * c.rect.pivot.x;
                                offset -= (c.size.x - pivot);
                                c.rect.anchoredPosition = c.rect.anchoredPosition.SetX(offset);
                                offset -= pivot + m_innerSpacing;
                            }
                            break;
                        case Justification.Center:
                            offset += (_contentSize.x + m_padding.left + m_padding.right) / 2;
                            
                            foreach(ChildInfo c in _children) {
                                // skip disabled/ignore items
                                if(CheckIgnoreElem(c))
                                    continue;
                                
                                SetAnchorX(c.rect, 0.5f);

                                float pivot = c.size.x * c.rect.pivot.x;
                                offset -= c.size.x - pivot;
                                c.rect.anchoredPosition = c.rect.anchoredPosition.SetX(offset - m_padding.right);
                                offset -= pivot + m_innerSpacing;
                            }
                            break;
                        case Justification.End:
                            offset += m_padding.right;
                            
                            foreach(ChildInfo c in _children) {
                                // skip disabled/ignore items
                                if(CheckIgnoreElem(c))
                                    continue;
                                
                                SetAnchorX(c.rect, 1);

                                float pivot = c.size.x * c.rect.pivot.x;
                                offset += c.size.x - pivot;
                                c.rect.anchoredPosition = c.rect.anchoredPosition.SetX(-offset);
                                offset += pivot + m_innerSpacing;
                            }
                            break;
                        case Justification.SpaceBetween:
                            offset += m_padding.right;
                            leftover = _rect.rect.size.x - _contentSize.x - m_padding.left - m_padding.right;
                            
                            if(_children.Count > 1)
                                spacing = leftover / (_children.Count-_ignoreCount-1);
                                
                            foreach(ChildInfo c in _children) {
                                // skip disabled/ignore items
                                if(CheckIgnoreElem(c))
                                    continue;
                                
                                SetAnchorX(c.rect, 1);

                                float pivot = c.size.x * c.rect.pivot.x;
                                offset += c.size.x - pivot;
                                c.rect.anchoredPosition = c.rect.anchoredPosition.SetX(-offset);
                                offset += pivot + spacing;
                            }
                            break;
                    }
                    break;
                // COLUMN/COLUMN-REVERSE -> CROSS AXIS
                case LayoutDirection.Column:
                case LayoutDirection.ColumnReverse:
                    switch(m_alignContent) {
                        case Alignment.Start:
                            offset += m_padding.left;
                            
                            foreach(ChildInfo c in _children) {
                                // skip disabled/ignore items
                                if(CheckIgnoreElem(c))
                                    continue;
                                
                                SetAnchorX(c.rect, 0);

                                float pivot = c.size.x * c.rect.pivot.x;
                                c.rect.anchoredPosition = c.rect.anchoredPosition.SetX(offset + pivot);
                            }
                            break;
                        case Alignment.Center:
                            foreach(ChildInfo c in _children) {
                                // skip disabled/ignore items
                                if(CheckIgnoreElem(c))
                                    continue;
                                
                                SetAnchorX(c.rect, 0.5f);

                                float pivot = c.size.x * c.rect.pivot.x;
                                c.rect.anchoredPosition = c.rect.anchoredPosition.SetX(m_padding.left/2 - m_padding.right/2 - (c.size.x/2 - pivot));
                            }
                            break;
                        case Alignment.End:
                            offset += m_padding.right;
                            
                            foreach(ChildInfo c in _children) {
                                // skip disabled/ignore items
                                if(CheckIgnoreElem(c))
                                    continue;
                                
                                SetAnchorX(c.rect, 1);

                                float pivot = c.size.x * c.rect.pivot.x;
                                c.rect.anchoredPosition = c.rect.anchoredPosition.SetX(-offset - (c.size.x - pivot));
                            }
                            break;
                    }
                    break;
            }
        }
        
        public void SetLayoutVertical() {
            Log("SetLayoutVertical");

            float offset = 0;
            float leftover;
            float spacing = 0;
            int index = 0;
            switch(m_direction) {
                // ROW/ROW-REVERSE -> CROSS AXIS
                case LayoutDirection.Row:
                case LayoutDirection.RowReverse:
                    switch(m_alignContent) {
                        case Alignment.Start:
                            offset += m_padding.top;
                            
                            foreach(ChildInfo c in _children) {
                                // skip disabled/ignore items
                                if(CheckIgnoreElem(c))
                                    continue;
                                
                                SetAnchorY(c.rect, 1);

                                float pivot = c.size.y * c.rect.pivot.y;
                                c.rect.anchoredPosition = c.rect.anchoredPosition.SetY(-offset - (c.size.y - pivot));
                            }
                            break;
                        case Alignment.Center:
                            foreach(ChildInfo c in _children) {
                                // skip disabled/ignore items
                                if(CheckIgnoreElem(c))
                                    continue;
                                
                                SetAnchorY(c.rect, 0.5f);

                                float pivot = c.size.y * c.rect.pivot.y;
                                c.rect.anchoredPosition = c.rect.anchoredPosition.SetY(m_padding.bottom/2 - m_padding.top/2 - (c.size.y/2 - pivot));
                            }
                            break;
                        case Alignment.End:
                            offset += m_padding.bottom;
                            
                            foreach(ChildInfo c in _children) {
                                // skip disabled/ignore items
                                if(CheckIgnoreElem(c))
                                    continue;
                                
                                SetAnchorY(c.rect, 0);
                    
                                c.rect.anchoredPosition = c.rect.anchoredPosition.SetY(offset + (c.size.y * c.rect.pivot.y));
                            }
                            break;
                    }
                    break;
                // COLUMN -> PRIMARY AXIS
                case LayoutDirection.Column:
                    switch(m_justifyContent) {
                        case Justification.Start:
                            offset -= m_padding.top;
                            
                            foreach(ChildInfo c in _children) {
                                // skip disabled/ignore items
                                if(CheckIgnoreElem(c))
                                    continue;
                                
                                SetAnchorY(c.rect, 1);

                                float pivot = c.size.y * c.rect.pivot.y;
                                offset -= c.size.y - pivot;
                                c.rect.anchoredPosition = c.rect.anchoredPosition.SetY(offset);
                                offset -= pivot + m_innerSpacing;
                            }
                            break;
                        case Justification.Center:
                            offset += (_contentSize.y + m_padding.top + m_padding.bottom) / 2;
                            
                            foreach(ChildInfo c in _children) {
                                // skip disabled/ignore items
                                if(CheckIgnoreElem(c))
                                    continue;
                                
                                SetAnchorY(c.rect, 0.5f);

                                float pivot = c.size.y * c.rect.pivot.y;
                                offset -= c.size.y - pivot;
                                c.rect.anchoredPosition = c.rect.anchoredPosition.SetY(offset - m_padding.top);
                                offset -= pivot + m_innerSpacing;
                            }
                            break;
                        case Justification.End:
                            offset += _contentSize.y;
                            
                            foreach(ChildInfo c in _children) {
                                // skip disabled/ignore items
                                if(CheckIgnoreElem(c))
                                    continue;
                                
                                SetAnchorY(c.rect, 0);

                                float pivot = c.size.y * c.rect.pivot.y;
                                offset -= c.size.y - pivot;
                                c.rect.anchoredPosition = c.rect.anchoredPosition.SetY(offset + m_padding.bottom);
                                offset -= pivot + m_innerSpacing;
                            }
                            break;
                        case Justification.SpaceBetween:
                            offset += m_padding.top;
                            leftover = _rect.rect.size.y - _contentSize.y - m_padding.top - m_padding.bottom;
                            
                            if(_children.Count > 1)
                                spacing = leftover / (_children.Count-_ignoreCount-1);
                                
                            foreach(ChildInfo c in _children) {
                                // skip disabled/ignore items
                                if(CheckIgnoreElem(c))
                                    continue;
                                
                                SetAnchorY(c.rect, 1);
                                
                                if(index != 0) {
                                    offset += spacing;
                                }

                                float pivot = c.size.y * c.rect.pivot.y;
                                offset += c.size.y - pivot;
                                c.rect.anchoredPosition = c.rect.anchoredPosition.SetY(-offset);
                                offset += pivot;
                            
                                index++;
                            }
                            break;
                    }
                    break;
                // COLUMN-REVERSE -> PRIMARY AXIS
                case LayoutDirection.ColumnReverse:
                    switch(m_justifyContent) {
                        case Justification.Start:
                            offset -= m_padding.top + _contentSize.y;
                            
                            foreach(ChildInfo c in _children) {
                                // skip disabled/ignore items
                                if(CheckIgnoreElem(c))
                                    continue;
                                
                                SetAnchorY(c.rect, 1);

                                float pivot = c.size.y * c.rect.pivot.y;
                                offset += pivot;
                                c.rect.anchoredPosition = c.rect.anchoredPosition.SetY(offset);
                                offset += c.size.y - pivot + m_innerSpacing;
                            }
                            break;
                        case Justification.Center:
                            offset -= (_contentSize.y + m_padding.top + m_padding.bottom) / 2;
                            
                            foreach(ChildInfo c in _children) {
                                // skip disabled/ignore items
                                if(CheckIgnoreElem(c))
                                    continue;
                                
                                SetAnchorY(c.rect, 0.5f);

                                float pivot = c.size.y * c.rect.pivot.y;
                                offset += pivot;
                                c.rect.anchoredPosition = c.rect.anchoredPosition.SetY(offset);
                                offset += c.size.y - pivot + m_innerSpacing;
                            }
                            break;
                        case Justification.End:
                            offset += m_padding.bottom;
                            
                            foreach(ChildInfo c in _children) {
                                // skip disabled/ignore items
                                if(CheckIgnoreElem(c))
                                    continue;
                                
                                SetAnchorY(c.rect, 0);

                                float pivot = c.size.y * c.rect.pivot.y;
                                offset += pivot;
                                c.rect.anchoredPosition = c.rect.anchoredPosition.SetY(offset);
                                offset += c.size.y - pivot + m_innerSpacing;
                            }
                            break;
                        case Justification.SpaceBetween:
                            offset += m_padding.bottom;
                            leftover = _rect.rect.size.y - _contentSize.y - m_padding.top - m_padding.bottom;
                            
                            if(_children.Count > 1)
                                spacing = leftover / (_children.Count-_ignoreCount-1);
                                
                            foreach(ChildInfo c in _children) {
                                // skip disabled/ignore items
                                if(CheckIgnoreElem(c))
                                    continue;
                                
                                SetAnchorY(c.rect, 0);

                                if(index != 0) {
                                    offset += spacing;
                                }
                                
                                float pivot = c.size.y * c.rect.pivot.y;
                                offset += pivot;
                                c.rect.anchoredPosition = c.rect.anchoredPosition.SetY(offset);
                                offset += c.size.y - pivot;

                                index++;
                            }
                            break;
                    }
                    break;
            }
        }
        #endregion
        
        #region Layout Internal
        private void Log(object msg) {
            if(m_log) Debug.Log($"[L:{gameObject.name}]: {msg}");
        }
        
        private bool CheckIgnoreElem(ChildInfo ci) {
            return !ci.enabled || ci.ignoreLayout;
        }

        private void SetAnchorX(RectTransform rt, float x) {
            rt.anchorMin = rt.anchorMin.SetX(x);
            rt.anchorMax = rt.anchorMax.SetX(x);
        }
        private void SetAnchorY(RectTransform rt, float y) {
            rt.anchorMin = rt.anchorMin.SetY(y);
            rt.anchorMax = rt.anchorMax.SetY(y);
        }
        #endregion
        
        #region LAYOUT PASSES
        public override float GrowSizingXCallback(float x) {
            base.GrowSizingXCallback(x);

            float ySize = _contentSize.y;
            GrowChildren(RectTransform.Axis.Horizontal);

            if(!Mathf.Approximately(_contentSize.y, ySize) && m_sizing.y == SizingMode.FitContent) {
                return _rect.rect.size.y;
            }
            
            return -1;
        }

        public override float GrowSizingYCallback(float y) {
            base.GrowSizingYCallback(y);

            float xSize = _contentSize.x;
            GrowChildren(RectTransform.Axis.Vertical);

            if(!Mathf.Approximately(_contentSize.x, xSize) && m_sizing.x == SizingMode.FitContent) {
                return _rect.rect.size.x;
            }
            
            return -1;
        }

        public void GrowChildren(RectTransform.Axis axis) {
            float size;
            float crossSize;
            float leftover;
            
            switch(axis) {
                case RectTransform.Axis.Horizontal:
                    if(_growChildCount.x > 0) {
                        Log($"growing {_growChildCount.x} children horizontally {_rect.rect.size}");

                        float count = _growChildCount.x;
                        switch(m_direction) {
                            case LayoutDirection.Row:
                            case LayoutDirection.RowReverse:
                                foreach(ChildInfo c in _children) {
                                    if(!c.li || CheckIgnoreElem(c))
                                        continue;
                                    
                                    leftover = _rect.rect.size.x - _contentSize.x - m_padding.left - m_padding.right;
                                    size = leftover / count;
                                    
                                    if(c.li.SizeMode.x == SizingMode.Grow) {
                                        c.size.x = size;
                                        _contentSize.x += size;
                                        float res = c.li.GrowSizingXCallback(size);
                                        _contentSize.y = Mathf.Max(res, _contentSize.y);
                                        
                                        if(res > 0)
                                            c.size.y = res;
                                        
                                        if(res > 0 && m_sizing.y == SizingMode.FitContent) {
                                            float newHeight = _contentSize.y + m_padding.top + m_padding.bottom;
                                            _rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, newHeight);
                                            Log($"resizing vertical axis based on callback response ({newHeight})");
                                        }

                                        count--;
                                    }
                                    else if(c.li is Layout l) {
                                        l.GrowChildren(RectTransform.Axis.Horizontal);
                                    }
                                }

                                break;
                            case LayoutDirection.Column:
                            case LayoutDirection.ColumnReverse:

                                foreach(ChildInfo c in _children) {
                                    if(!c.li || CheckIgnoreElem(c))
                                        continue;

                                    crossSize = _rect.rect.size.x - m_padding.left - m_padding.right;
                                    size = crossSize;
                                    
                                    if(c.li.SizeMode.x == SizingMode.Grow) {
                                        c.size.x = size;
                                        _contentSize.x = Mathf.Max(size, _contentSize.x);
                                        float res = c.li.GrowSizingXCallback(size);
                                        _contentSize.y = Mathf.Max(res, _contentSize.y);

                                        if(res > 0)
                                            c.size.y = res;
                                        
                                        if(res > 0 && m_sizing.y == SizingMode.FitContent) {
                                            float newHeight = _contentSize.y + m_padding.top + m_padding.bottom;
                                            _rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, newHeight);
                                            Log($"resizing vertical axis based on callback response ({newHeight})");
                                        }
                                    }
                                    else if(c.li is Layout l) {
                                        l.GrowChildren(RectTransform.Axis.Horizontal);
                                    }
                                }
                                
                                break;
                        }
                    }
                    // keep grow sizing propagation going
                    else {
                        foreach(ChildInfo c in _children) {
                            if(!c.li)
                                continue;
                            
                            if(c.li is Layout layout)
                                layout.GrowChildren(RectTransform.Axis.Horizontal);
                        }
                    }
                    break;
                case RectTransform.Axis.Vertical:
                    if(_growChildCount.y > 0) {
                        Log($"growing {_growChildCount.y} children vertically {_rect.rect.size}");

                        float count = _growChildCount.y;
                        switch(m_direction) {
                            case LayoutDirection.Row:
                            case LayoutDirection.RowReverse:
                                
                                foreach(ChildInfo c in _children) {
                                    if(!c.li || CheckIgnoreElem(c))
                                        continue;
                                    
                                    crossSize = _rect.rect.size.y - m_padding.top - m_padding.bottom;
                                    size = crossSize;

                                    if(c.li.SizeMode.y == SizingMode.Grow) {
                                        c.size.y = size;
                                        _contentSize.y = Mathf.Max(size, _contentSize.y);
                                        float res = c.li.GrowSizingYCallback(size);
                                        _contentSize.x = Mathf.Max(res, _contentSize.x);
                                        
                                        if(res > 0)
                                            c.size.x = res;
                                        
                                        if(res > 0 && m_sizing.x == SizingMode.FitContent) {
                                            float newWidth = _contentSize.x + m_padding.left + m_padding.right;
                                            _rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, newWidth);
                                            Log($"resizing vertical axis based on callback response ({newWidth})");
                                        }
                                    }
                                    else if(c.li is Layout l) {
                                        l.GrowChildren(RectTransform.Axis.Vertical);
                                    }
                                }

                                break;
                            case LayoutDirection.Column:
                            case LayoutDirection.ColumnReverse:

                                foreach(ChildInfo c in _children) {
                                    if(!c.li || CheckIgnoreElem(c))
                                        continue;
                                    
                                    leftover = _rect.rect.size.y - _contentSize.y - m_padding.top - m_padding.bottom;
                                    size = leftover / count;
                                    
                                    if(c.li.SizeMode.y == SizingMode.Grow) {
                                        c.size.y = size;
                                        _contentSize.y += size;
                                        float res = c.li.GrowSizingYCallback(size);
                                        _contentSize.x = Mathf.Max(res, _contentSize.x);
                                        
                                        if(res > 0)
                                            c.size.x = res;
                                        
                                        if(res > 0 && m_sizing.x == SizingMode.FitContent) {
                                            float newWidth = _contentSize.x + m_padding.left + m_padding.right;
                                            _rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, newWidth);
                                            Log($"resizing vertical axis based on callback response ({newWidth})");
                                        }

                                        count--;
                                    }
                                    else if(c.li is Layout l) {
                                        l.GrowChildren(RectTransform.Axis.Vertical);
                                    }
                                }
                                
                                break;
                        }
                    }
                    // keep grow sizing propagation going
                    else {
                        foreach(ChildInfo c in _children) {
                            if(!c.li)
                                continue;
                            
                            if(c.li is Layout layout)
                                layout.GrowChildren(RectTransform.Axis.Vertical);
                        }
                    }
                    break;
            }
        }
        #endregion
        
        public int CompareTo(Layout other) {
            if(_depth < other._depth) {
                return 1;
            }
            if(_depth == other._depth) {
                return 0;
            }
            
            return -1;
        }

        public override void SetDirty() {
            base.SetDirty();
            _dirty = true;
        }
        
        public void RefreshChildCache() {
            _children.Clear();
            
            int childCount = transform.childCount;
            Log($"Refreshing child cache - {childCount} children detected");
            
            for(int i = 0; i < childCount; i++) {
                RectTransform rt = transform.GetChild(i).GetComponent<RectTransform>();
                
                Log($"Adding child - size: {rt.rect.size}");
                
                LayoutItem li = rt.GetComponent<LayoutItem>();
                
                _children.Add(
                    new ChildInfo {
                        index = i,
                        rect = rt,
                        li = li,
                        size = rt.rect.size * (m_ignoreChildScale ? Vector2.one : rt.localScale),
                        enabled = rt.gameObject.activeInHierarchy,
                        ignoreLayout = li && li.IgnoreLayout
                    }
                );
            }
            
            LayoutRebuilder.MarkLayoutForRebuild(_rect);
        }
    }
}
