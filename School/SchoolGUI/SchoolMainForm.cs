﻿using Aga.Controls.Tree;
using Aga.Controls.Tree.NodeControls;
using GoodAI.BrainSimulator.Forms;
using GoodAI.Modules.School.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using YAXLib;

namespace GoodAI.School.GUI
{
    [BrainSimUIExtension]
    public partial class SchoolMainForm : DockContent
    {
        public SchoolAddTaskForm AddTaskView { get; private set; }
        public SchoolRunForm RunView { get; private set; }
        private YAXSerializer m_serializer;
        private TreeModel m_model;
        private string m_lastOpenedFile;

        public SchoolMainForm()
        {
            m_serializer = new YAXSerializer(typeof(SchoolCurriculum));
            AddTaskView = new SchoolAddTaskForm();
            RunView = new SchoolRunForm();

            InitializeComponent();

            m_model = new TreeModel();
            tree.Model = m_model;
            tree.Refresh();

            checkBoxAutosave.Checked = Properties.School.Default.AutosaveEnabled;
            m_lastOpenedFile = Properties.School.Default.LastOpenedFile;
            if (LoadCurriculum(m_lastOpenedFile))
                saveFileDialog1.FileName = m_lastOpenedFile;

            UpdateButtons();
        }

        private void SchoolMainForm_Load(object sender, System.EventArgs e) { }

        #region Nodes classes

        public class SchoolTreeNode : Node
        {
            public SchoolTreeNode() { }
            public SchoolTreeNode(string text) : base(text) { }
            public bool Enabled { get; set; }
        }

        public class CurriculumNode : SchoolTreeNode
        {
            public CurriculumNode(string text) : base(text) { }
        }

        public class LearningTaskNode : SchoolTreeNode
        {
            private readonly ILearningTask m_task;

            public LearningTaskNode(ILearningTask task)
            {
                m_task = task;
                Enabled = true;
            }

            public string Name
            {
                get
                {
                    return TaskType.Name;
                }
            }

            public string World
            {
                get
                {
                    return WorldType.Name;
                }
            }

            public Type TaskType
            {
                get
                {
                    return m_task.GetType();
                }
            }

            public Type WorldType
            {
                get
                {
                    return m_task.GenericWorld.GetType();
                }
            }

            public int Steps { get; set; }
            public float Time { get; set; }
            public string Status { get; set; }

            public override string Text
            {
                get
                {
                    return m_task.GetType().Name + " (" + m_task.GenericWorld.GetType().Name + ")";
                }
            }
        }

        #endregion

        #region Curricula

        private CurriculumNode AddCurriculum()
        {
            CurriculumNode node = new CurriculumNode("Curr" + m_model.Nodes.Count.ToString());
            m_model.Nodes.Add(node);
            return node;
        }

        private bool LoadCurriculum(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            string xmlCurr;
            try { xmlCurr = File.ReadAllText(filePath); }
            catch (FileNotFoundException e) { return false; }

            try
            {
                SchoolCurriculum curr = (SchoolCurriculum)m_serializer.Deserialize(xmlCurr);
                CurriculumNode node = CurriculumDataToCurriculumNode(curr);
                m_model.Nodes.Add(node);
            }
            catch (YAXException) { return false; }

            Properties.School.Default.LastOpenedFile = filePath;
            Properties.School.Default.Save();

            return true;
        }

        private SchoolCurriculum CurriculumNodeToCurriculumData(CurriculumNode node)
        {
            SchoolCurriculum data = new SchoolCurriculum();
            data.Name = node.Text;

            foreach (LearningTaskNode taskNode in node.Nodes)
                data.AddLearningTask(taskNode.TaskType, taskNode.WorldType);

            return data;
        }

        private CurriculumNode CurriculumDataToCurriculumNode(SchoolCurriculum data)
        {
            CurriculumNode node = new CurriculumNode(data.Name);

            foreach (ILearningTask task in data)
            {
                // TODO: World name can be displayed through reflection OR once World param is in ILearningTask (or SchoolCurriculum is restricted to AbstractLTs)
                LearningTaskNode taskNode = new LearningTaskNode(task);
                taskNode.Enabled = true;
                node.Nodes.Add(taskNode);
            }

            return node;
        }

        private List<LearningTaskNode> CurriculumDataToLTData(SchoolCurriculum curriculum)
        {
            List<LearningTaskNode> result = new List<LearningTaskNode>();
            foreach (ILearningTask task in curriculum)
            {
                LearningTaskNode data = new LearningTaskNode(task);
                result.Add(data);
            }

            return result;
        }

        #endregion

        #region UI

        private void ApplyToAll(Control parent, Action<Control> apply)
        {
            foreach (Control control in parent.Controls)
            {
                if (control.HasChildren)
                    ApplyToAll(control, apply);
                apply(control);
            }
        }

        private void SetButtonsEnabled(Control control, bool value)
        {
            Action<Control> setBtns = (x) =>
            {
                Button b = x as Button;
                if (b != null)
                    b.Enabled = value;
            };
            ApplyToAll(control, setBtns);
        }

        private void DisableButtons(Control control)
        {
            SetButtonsEnabled(control, false);
        }

        private void EnableButtons(Control control)
        {
            SetButtonsEnabled(control, true);
        }

        private void UpdateButtons()
        {
            EnableButtons(this);

            if (!tree.AllNodes.Any())
                btnSave.Enabled = btnSaveAs.Enabled = false;

            if (tree.SelectedNode == null)
            {
                btnDeleteCurr.Enabled = btnRun.Enabled = btnDetailsCurr.Enabled = false;
                DisableButtons(groupBoxTask);
                return;
            }

            SchoolTreeNode selected = tree.SelectedNode.Tag as SchoolTreeNode;
            Debug.Assert(selected != null);

            if (selected is CurriculumNode)
                btnDeleteTask.Enabled = btnDetailsTask.Enabled = false;
        }

        #endregion

        #region DragDrop

        private void tree_ItemDrag(object sender, System.Windows.Forms.ItemDragEventArgs e)
        {
            tree.DoDragDropSelectedNodes(DragDropEffects.Move);
        }

        private void tree_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(TreeNodeAdv[])) && tree.DropPosition.Node != null)
            {
                TreeNodeAdv[] nodes = e.Data.GetData(typeof(TreeNodeAdv[])) as TreeNodeAdv[];
                TreeNodeAdv parent = tree.DropPosition.Node;
                if (tree.DropPosition.Position != NodePosition.Inside)
                    parent = parent.Parent;

                foreach (TreeNodeAdv node in nodes)
                    if (!CheckNodeParent(parent, node))
                    {
                        e.Effect = DragDropEffects.None;
                        return;
                    }

                e.Effect = e.AllowedEffect;
            }
        }

        private bool CheckNodeParent(TreeNodeAdv parent, TreeNodeAdv node)
        {
            while (parent != null)
            {
                if (node == parent)
                    return false;
                else
                    parent = parent.Parent;
            }
            return true;
        }

        private void tree_DragDrop(object sender, DragEventArgs e)
        {
            tree.BeginUpdate();

            TreeNodeAdv[] nodes = (TreeNodeAdv[])e.Data.GetData(typeof(TreeNodeAdv[]));
            Node dropNode = tree.DropPosition.Node.Tag as Node;
            if (tree.DropPosition.Position == NodePosition.Inside)
            {
                foreach (TreeNodeAdv n in nodes)
                {
                    (n.Tag as Node).Parent = dropNode;
                }
                tree.DropPosition.Node.IsExpanded = true;
            }
            else
            {
                Node parent = dropNode.Parent;
                Node nextItem = dropNode;
                if (tree.DropPosition.Position == NodePosition.After)
                    nextItem = dropNode.NextNode;

                foreach (TreeNodeAdv node in nodes)
                    (node.Tag as Node).Parent = null;

                int index = -1;
                index = parent.Nodes.IndexOf(nextItem);
                foreach (TreeNodeAdv node in nodes)
                {
                    Node item = node.Tag as Node;
                    if (index == -1)
                        parent.Nodes.Add(item);
                    else
                    {
                        parent.Nodes.Insert(index, item);
                        index++;
                    }
                }
            }

            tree.EndUpdate();
        }

        #endregion

        #region Button clicks

        private void btnNewCurr_Click(object sender, EventArgs e)
        {
            CurriculumNode newCurr = AddCurriculum();
        }

        private void btnDeleteCurr_Click(object sender, EventArgs e)
        {
            if (tree.SelectedNode.Tag is CurriculumNode)
            {
                DeleteNode(sender, e);
                return;
            }
            Node parent = (tree.SelectedNode.Tag as Node).Parent;
            if (parent != null && parent is CurriculumNode)
                parent.Parent = null;
        }

        private void btnNewTask_Click(object sender, EventArgs e)
        {
            if (tree.SelectedNode == null || !(tree.SelectedNode.Tag is CurriculumNode))
                return;

            AddTaskView.ShowDialog(this);
            if (AddTaskView.ResultTask == null)
                return;

            ILearningTask task = LearningTaskFactory.CreateLearningTask(AddTaskView.ResultTaskType, AddTaskView.ResultWorldType);
            LearningTaskNode newTask = new LearningTaskNode(task);
            (tree.SelectedNode.Tag as Node).Nodes.Add(newTask);
            tree.SelectedNode.IsExpanded = true;
        }

        private void DeleteNode(object sender, EventArgs e)
        {
            (tree.SelectedNode.Tag as Node).Parent = null;
        }

        private void checkBoxAutosave_CheckedChanged(object sender, EventArgs e)
        {
            Properties.School.Default.AutosaveEnabled = checkBoxAutosave.Checked;
            Properties.School.Default.Save();
        }

        private void btnRun_Click(object sender, EventArgs e)
        {
            OpenFloatingOrActivate(RunView, DockPanel);
            List<LearningTaskNode> data = new List<LearningTaskNode>();
            foreach (LearningTaskNode ltNode in (tree.SelectedNode.Tag as CurriculumNode).Nodes)
                data.Add(ltNode);
            RunView.Data = data;
            RunView.UpdateData();
        }

        private bool AddFileContent(bool clearWorkspace = false)
        {
            if (openFileDialog1.ShowDialog() != DialogResult.OK)
                return false;
            if (clearWorkspace)
                m_model.Nodes.Clear();
            LoadCurriculum(openFileDialog1.FileName);
            return true;
        }

        private void btnOpen_Click(object sender, EventArgs e)
        {
            AddFileContent(true);
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            if (saveFileDialog1.FileName != string.Empty)
                SaveProject(saveFileDialog1.FileName);
            else
                SaveProjectAs(sender, e);  // ask for file name and then save the project
        }

        private void btnImport_Click(object sender, EventArgs e)
        {
            AddFileContent();
        }

        #endregion

        #region (De)serialization

        private void SaveProject(string path)
        {
            SchoolCurriculum test = CurriculumNodeToCurriculumData(tree.SelectedNode.Tag as CurriculumNode);
            string xmlCurr = m_serializer.Serialize(test);
            File.WriteAllText(path, xmlCurr);
        }

        private void SaveProjectAs(object sender, EventArgs e)
        {
            if (saveFileDialog1.ShowDialog() != DialogResult.OK)
                return;

            SaveProject(saveFileDialog1.FileName);
            Properties.School.Default.LastOpenedFile = saveFileDialog1.FileName;
            Properties.School.Default.Save();
        }

        #endregion

        // almost same as Mainform.OpenFloatingOrActivate - refactor?
        private void OpenFloatingOrActivate(DockContent view, DockPanel panel)
        {
            if ((view.DockAreas & DockAreas.Float) > 0 && !view.Created)
            {
                Size viewSize = new Size(view.Bounds.Size.Width, view.Bounds.Size.Height);
                view.Show(panel, DockState.Float);
                view.FloatPane.FloatWindow.Size = viewSize;
            }
            else
            {
                view.Activate();
            }
        }

        private void tree_SelectionChanged(object sender, EventArgs e)
        {
            UpdateButtons();
        }

        private void nodeTextBox1_DrawText(object sender, DrawTextEventArgs e)
        {
            if (e.Node.IsSelected)
                e.Font = new System.Drawing.Font(e.Font, FontStyle.Bold);
        }
    }
}
