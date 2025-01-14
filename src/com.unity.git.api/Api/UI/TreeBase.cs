﻿using Unity.VersionControl.Git;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.VersionControl.Git
{
    public enum CheckState
    {
        Empty,
        Checked,
        Mixed
    }

    public interface ITreeNode
    {
        string Path { get; set; }
        string Label { get; set; }
        int Level { get; set; }
        bool IsContainer { get; set; }
        bool IsFolder { get; set; }
        bool IsFolderOrContainer { get; }
        bool IsCollapsed { get; set; }
        bool IsHidden { get; set; }
        bool IsActive { get; set; }
        bool TreeIsCheckable { get; set; }
        CheckState CheckState { get; set; }
    }

    public abstract class TreeBase<TNode, TData> where TNode : class, ITreeNode where TData : struct, ITreeData
    {
        protected ILogging Logger { get; }

        protected TreeBase()
        {
            Logger = LogHelper.GetLogger(GetType());
        }

        public void Load(IEnumerable<TData> treeDatas)
        {
            var collapsedFolders = new HashSet<string>(GetCollapsedFolders());
            var checkedFiles = new HashSet<string>(GetCheckedFiles());
            var folders = new HashSet<string>();

            string selectedNodePath = SelectedNodePath;
            string pathSeparator = PathSeparator;

            int displayRootLevel = DisplayRootNode ? 1 : 0;
            int hideChildrenBelowLevel = 0;

            bool isCheckable = IsCheckable;
            bool isSelected = IsSelectable && selectedNodePath != null && Title == selectedNodePath;
            bool hideChildren = false;
            TNode lastAddedNode = null;

            Clear();
            AddNode(Title, Title, -1 + displayRootLevel, true, false, false, false, isSelected, false, null);

            foreach (var treeData in treeDatas)
            {
                var parts = treeData.Path.Split(new[] { pathSeparator }, StringSplitOptions.None);
                for (var level = 0; level < parts.Length; level++)
                {
                    var label = parts[level];
                    var nodePath = String.Join(pathSeparator, parts, 0, level + 1);
                    var isFolder = level < parts.Length - 1;
                    var parentIsPromoted = false;

                    if (lastAddedNode != null)
                    {
                        if (!lastAddedNode.IsFolder)
                        {
                            if (PromoteNode(lastAddedNode, label))
                            {
                                parentIsPromoted = true;
                                lastAddedNode.IsContainer = true;
                            }
                        }
                    }

                    var alreadyExists = folders.Contains(nodePath);
                    if (!alreadyExists)
                    {
                        var nodeIsHidden = false;
                        if (hideChildren)
                        {
                            if (level + 1 <= hideChildrenBelowLevel)
                            {
                                hideChildren = false;
                            }
                            else
                            {
                                nodeIsHidden = true;
                            }
                        }

                        var isActive = false;
                        var isChecked = false;
                        var nodeIsCollapsed = false;
                        TData? treeNodeTreeData = null;

                        if (isFolder)
                        {
                            folders.Add(nodePath);

                            if (collapsedFolders.Contains(nodePath))
                            {
                                nodeIsCollapsed = true;

                                if (!hideChildren)
                                {
                                    hideChildren = true;
                                    hideChildrenBelowLevel = level + 1;
                                }
                            }
                        }
                        else
                        {
                            isActive = treeData.IsActive;
                            treeNodeTreeData = treeData;
                            isChecked = isCheckable && (checkedFiles.Contains(nodePath) || treeData.IsChecked);
                        }

                        isSelected = selectedNodePath != null && nodePath == selectedNodePath;

                        lastAddedNode = AddNode(nodePath, label, level + displayRootLevel + (parentIsPromoted ? 1 : 0), isFolder, isActive, nodeIsHidden, nodeIsCollapsed, isSelected, isChecked, treeNodeTreeData);
                    }
                }
            }

            if (isCheckable && checkedFiles.Any())
            {
                var nodes = Nodes;
                for (var index = nodes.Count - 1; index >= 0; index--)
                {
                    var node = nodes[index];
                    if (node.Level >= 0 && node.IsFolderOrContainer)
                    {
                        bool? anyChecked = null;
                        bool? allChecked = null;

                        for (var i = index + 1; i < nodes.Count; i++)
                        {
                            var nodeCompare = nodes[i];
                            if (nodeCompare.Level < node.Level + 1)
                            {
                                break;
                            }

                            var nodeIsChecked = nodeCompare.CheckState == CheckState.Checked;
                            allChecked = (allChecked ?? true) && nodeIsChecked;
                            anyChecked = (anyChecked ?? false) || nodeIsChecked;
                        }

                        node.CheckState = anyChecked.Value 
                            ? (allChecked.Value ? CheckState.Checked : CheckState.Mixed)
                            : CheckState.Empty;
                    }
                }
            }
        }

        protected bool PromoteNode(TNode previouslyAddedNode, string nextLabel)
        {
            if (!PromoteMetaFiles)
            {
                return false;
            }

            if (previouslyAddedNode == null)
            {
                return false;
            }

            if (!nextLabel.EndsWith(".meta"))
            {
                return false;
            }

            var substring = nextLabel.Substring(0, nextLabel.Length - 5);
            return previouslyAddedNode.Label == substring;
        }

        public void SetCheckStateOnAll(bool isChecked)
        {
            foreach (var node in Nodes)
            {
                SetCheckStateOnNode(node, isChecked);
            }
        }

        protected TNode AddNode(string path, string label, int level, bool isFolder, bool isActive, bool isHidden, bool isCollapsed, bool isSelected, bool isChecked, TData? treeData)
        {
            var node = CreateTreeNode(path, label, level, isFolder, isActive, isHidden, isCollapsed, isChecked, treeData);

            SetNodeIcon(node);
            Nodes.Add(node);

            if (isSelected)
            {
                SelectedNode = node;
            }

            return node;
        }

        protected virtual void Clear()
        {
            Nodes.Clear();
            SelectedNode = null;
        }

        protected void ToggleNodeVisibility(int idx, TNode node)
        {
            var nodeLevel = node.Level;
            node.IsCollapsed = !node.IsCollapsed;
            idx++;
            for (; idx < Nodes.Count && Nodes[idx].Level > nodeLevel; idx++)
            {
                Nodes[idx].IsHidden = node.IsCollapsed;
                if (Nodes[idx].IsFolderOrContainer && !node.IsCollapsed && Nodes[idx].IsCollapsed)
                {
                    var level = Nodes[idx].Level;
                    for (idx++; idx < Nodes.Count && Nodes[idx].Level > level; idx++)
                    { }

                    idx--;
                }
            }

            if (SelectedNode != null && SelectedNode.IsHidden)
            {
                SelectedNode = node;
            }
        }

        protected void ToggleNodeChecked(int idx, TNode node)
        {
            CheckState checkState;
            var isChecked = false;
            switch (node.CheckState)
            {
                case CheckState.Mixed:
                case CheckState.Empty:
                    checkState = CheckState.Checked;
                    isChecked = true;
                    break;

                case CheckState.Checked:
                    checkState = CheckState.Empty;
                    break;

                default:
                    throw new ArgumentOutOfRangeException("Unknown CheckState");
            }

            SetCheckStateOnNode(node, checkState);

            if (node.IsFolderOrContainer)
            {
                ToggleChildrenChecked(idx, node, isChecked);
            }

            ToggleParentFolderAndContainersChecked(idx, node, checkState);
        }

        private void ToggleChildrenChecked(int idx, TNode node, bool isChecked)
        {
            for (var i = idx + 1; i < Nodes.Count && node.Level < Nodes[i].Level; i++)
            {
                var childNode = Nodes[i];

                SetCheckStateOnNode(childNode, isChecked);

                if (childNode.IsFolderOrContainer)
                {
                    ToggleChildrenChecked(i, childNode, isChecked);
                }
            }
        }

        public List<TNode> GetLeafNodes(TNode parentNode)
        {
            var index = Nodes.IndexOf(parentNode);
            return GetLeafNodes(parentNode, index);
        }

        private List<TNode> GetLeafNodes(TNode node, int idx)
        {
            var results = new List<TNode>();
            for (var i = idx + 1; i < Nodes.Count && node.Level < Nodes[i].Level; i++)
            {
                var childNode = Nodes[i];
                if (!childNode.IsFolder)
                {
                    results.Add(childNode);
                }
            }
            return results;
        }

        private void SetCheckStateOnNode(TNode node, bool setChecked)
        {
            SetCheckStateOnNode(node, setChecked ? CheckState.Checked : CheckState.Empty);
        }

        private void SetCheckStateOnNode(TNode node, CheckState setCheckState)
        {
            var isChecked = setCheckState == CheckState.Checked
                || setCheckState == CheckState.Mixed;

            var wasChecked = node.CheckState == CheckState.Checked;

            node.CheckState = setCheckState;

            if (!node.IsFolder)
            {
                if (isChecked && !wasChecked)
                {
                    AddCheckedNode(node);
                }
                else if (!isChecked && wasChecked)
                {
                    RemoveCheckedNode(node);
                }
            }
        }

        private void ToggleParentFolderAndContainersChecked(int idx, TNode node, CheckState checkState)
        {
            var isChecked = checkState != CheckState.Empty;
            while (true)
            {
                if (node.Level > 0)
                {
                    var siblingsInSameState = true;
                    var firstSiblingIndex = idx;

                    for (var i = idx - 1; i > 0 && node.Level <= Nodes[i].Level; i--)
                    {
                        var previousNode = Nodes[i];
                        if (node.Level < previousNode.Level)
                        {
                            continue;
                        }

                        firstSiblingIndex = i;

                        if (siblingsInSameState)
                        {
                            var previousNodeIsChecked = previousNode.CheckState == CheckState.Checked;

                            if (isChecked != previousNodeIsChecked)
                            {
                                siblingsInSameState = false;
                            }
                        }
                    }

                    if (siblingsInSameState)
                    {
                        for (var i = idx + 1; i < Nodes.Count && node.Level <= Nodes[i].Level; i++)
                        {
                            var followingNode = Nodes[i];
                            if (node.Level < followingNode.Level)
                            {
                                continue;
                            }

                            var followingNodeIsChecked = followingNode.CheckState == CheckState.Checked;
                            if (isChecked != followingNodeIsChecked)
                            {
                                siblingsInSameState = false;
                                break;
                            }
                        }
                    }

                    var parentIndex = firstSiblingIndex - 1;
                    var parentNode = Nodes[parentIndex];

                    var parentNodeState =
                        siblingsInSameState
                            ? node.CheckState
                            : CheckState.Mixed;

                    SetCheckStateOnNode(parentNode, parentNodeState);

                    idx = parentIndex;
                    node = parentNode;
                    continue;
                }

                break;
            }
        }

        public abstract IEnumerable<string> GetCheckedFiles();
        protected abstract IEnumerable<string> GetCollapsedFolders();
        protected abstract void RemoveCheckedNode(TNode node);
        protected abstract void AddCheckedNode(TNode node);
        protected abstract TNode CreateTreeNode(string path, string label, int level, bool isFolder, bool isActive, bool isHidden, bool isCollapsed, bool isChecked, TData? treeData);

        protected abstract void SetNodeIcon(TNode node);

        public string SelectedNodePath => SelectedNode?.Path;
        public abstract TNode SelectedNode { get; set; }
        protected abstract List<TNode> Nodes { get; }
        public abstract string Title { get; set; }
        public abstract bool DisplayRootNode { get; set; }
        public abstract bool IsSelectable { get; set; }
        public abstract bool IsCheckable { get; set; }
        public abstract string PathSeparator { get; set; }
        protected abstract bool PromoteMetaFiles { get; }
    }
}
