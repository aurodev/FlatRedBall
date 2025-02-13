﻿using FlatRedBall;
using FlatRedBall.Graphics;
using FlatRedBall.Math;
using FlatRedBall.Math.Geometry;
using FlatRedBall.Utilities;
using GlueControl.Managers;
using GlueControl.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GlueControl.Editing
{
    class CopyPasteManager
    {
        #region Fields/Properties

        List<INameable> CopiedObjects
        {
            get; set;
        } = new List<INameable>();

        List<NamedObjectSave> CopiedNamedObjects
        {
            get; set;
        } = new List<NamedObjectSave>();

        static GlueElement CopiedObjectsOwner;

        #endregion

        public void DoHotkeyLogic(List<INameable> selectedObjects, List<NamedObjectSave> selectedNamedObjects, IStaticPositionable itemGrabbed)
        {
            var keyboard = FlatRedBall.Input.InputManager.Keyboard;

            if (keyboard.IsCtrlDown)
            {
                if (keyboard.KeyPushed(Keys.C))
                {
                    HandleCopy(selectedObjects, selectedNamedObjects);
                }
                if (keyboard.KeyPushed(Keys.V) && CopiedObjects != null)
                {
                    if (CopiedObjectsOwner == null)
                    {
                        throw new InvalidOperationException("The CopiedObjectsOwner should not be null...");
                    }
                    HandlePaste(itemGrabbed, selectedNamedObjects, CopiedNamedObjects, CopiedObjects, CopiedObjectsOwner);
                }
                if (keyboard.KeyPushed(Keys.D) && selectedNamedObjects.Count > 0)
                {
                    var tempCopiedNamedObjects = new List<NamedObjectSave>();
                    tempCopiedNamedObjects.AddRange(selectedNamedObjects);

                    // create a temp list for the selected named objects
                    var tempCopiedObjects = new List<INameable>();
                    tempCopiedObjects.AddRange(selectedObjects);

                    HandlePaste(itemGrabbed, selectedNamedObjects, tempCopiedNamedObjects, tempCopiedObjects, GlueState.Self.CurrentElement);
                }
            }
        }

        #region Copy

        private void HandleCopy(List<INameable> selectedObjects, List<NamedObjectSave> selectedNamedObjects)
        {
            CopiedObjects.Clear();
            CopiedNamedObjects.Clear();

            CopiedObjects.AddRange(selectedObjects);
            CopiedNamedObjects.AddRange(selectedNamedObjects);

#if HasGum && SupportsEditMode
            string message = "";
            if(selectedNamedObjects.Count == 1)
            {
                message = $"Copied {selectedNamedObjects[0]} to clipboard";
            }
            else
            {
                message = $"Copied {selectedNamedObjects.Count} objects to clipboard";
            }
            FlatRedBall.Forms.Controls.Popups.ToastManager.Show(message);
            if (GlueState.Self.CurrentElement == null && (CopiedNamedObjects.Count > 0 || CopiedObjects.Count > 0))
            {
                throw new InvalidOperationException();
            }
#endif

            CopiedObjectsOwner = GlueState.Self.CurrentElement;
        }

        #endregion

        #region Paste

        (float x, float y) GetXY(NamedObjectSave nos)
        {
            var xAsObject = nos.InstructionSaves.FirstOrDefault(item => item.Member == "X")?.Value;
            var yAsObject = nos.InstructionSaves.FirstOrDefault(item => item.Member == "Y")?.Value;
            float x = 0;
            float y = 0;
            if (xAsObject is float asFloatX)
            {
                x = asFloatX;
            }
            if (yAsObject is float asFloatY)
            {
                y = asFloatY;
            }
            return (x, y);
        }

        private async void HandlePaste(IStaticPositionable itemGrabbed, List<NamedObjectSave> selectedNamedObjects, List<NamedObjectSave> copiedNamedObjects, List<INameable> copiedObjects, GlueElement copiedObjectsOwner)
        {
            var currentElement = GlueState.Self.CurrentElement;

            GetOffsetForPasting(itemGrabbed, selectedNamedObjects, out float? offsetX, out float? offsetY);

            List<Task> tasksToWait = new List<Task>();

            Debug.WriteLine($"Looping through CopiedNamedObjects with count {copiedNamedObjects.Count}");

            var positionOnPaste =
                                new Vector3(FlatRedBall.Gui.GuiManager.Cursor.WorldXAt(0), FlatRedBall.Gui.GuiManager.Cursor.WorldYAt(0), 0); // todo - make this better for 3D


            if (EditingManager.Self.IsSnappingEnabled && EditingManager.Self.SnapSize != 0)
            {
                var snapSize = EditingManager.Self.SnapSize;

                positionOnPaste.X = MathFunctions.RoundFloat(positionOnPaste.X, snapSize);
                positionOnPaste.Y = MathFunctions.RoundFloat(positionOnPaste.Y, snapSize);
            }


            var copyResponse = await GlueCommands.Self.GluxCommands.CopyNamedObjectListIntoElement(
                copiedNamedObjects,
                copiedObjectsOwner,
                currentElement);

            var newNamedObjects = copyResponse
                .Select(item => item.Data)
                .Where(item => item != null)
                .ToList();

            Debug.WriteLine($"Moving newNameObjects count {newNamedObjects.Count}" +
                $" with offset {offsetX}, {offsetY}");

            List<Vector3> newPositionedOrdered = new List<Vector3>();
            var oldPositionables = copiedObjects
                .Select(item => item as IStaticPositionable)
                .ToArray();

            if (oldPositionables.Length > 0)
            {

                var minX = oldPositionables.Min(item => item.X);
                var minY = oldPositionables.Min(item => item.Y);
                var maxX = oldPositionables.Max(item => item.X);
                var maxY = oldPositionables.Max(item => item.Y);

                var offsetForCenteringX = 1 * (maxX - minX) / 2.0f;
                var offsetForCenteringY = 1 * (maxY - minY) / 2.0f;

                // Start with the cursor position, subtract the offset to get the bottom-left most position...
                var snappedLeft = positionOnPaste.X - offsetForCenteringX;
                var snappedBottom = positionOnPaste.Y - offsetForCenteringY;
                if (EditingManager.Self.IsSnappingEnabled && EditingManager.Self.SnapSize != 0)
                {
                    var snapSize = EditingManager.Self.SnapSize;

                    snappedLeft = MathFunctions.RoundFloat(snappedLeft, snapSize);
                    snappedBottom = MathFunctions.RoundFloat(snappedBottom, snapSize);
                }

                List<NosVariableAssignment> variableAssignments = new List<NosVariableAssignment>();
                EditingManager.Self.Select((string)null);

                for (int i = 0; i < newNamedObjects.Count; i++)
                {
                    var newNos = newNamedObjects[i];

                    // Add the position of this object relative to its group's bototm left
                    var offsetFromMinX = oldPositionables[i].X - minX;
                    var offsetFromMinY = oldPositionables[i].Y - minY;
                    var x = snappedLeft + offsetFromMinX;
                    var y = snappedBottom + offsetFromMinY;

                    var newINameable = EditingManager.Self.GetObjectByName(newNos.InstanceName);
                    if (newINameable is PositionedObject newPositionedObject && newPositionedObject.Parent != null)
                    {
                        x = x - newPositionedObject.Parent.X;
                        y = y - newPositionedObject.Parent.Y;
                    }

                    // place this where the cursor is - assuming the cursor is in the window
                    variableAssignments.Add(new NosVariableAssignment
                    {
                        NamedObjectSave = newNos,
                        VariableName = "X",
                        Value = x
                    });
                    variableAssignments.Add(new NosVariableAssignment
                    {
                        NamedObjectSave = newNos,
                        VariableName = "Y",
                        Value = y
                    });

                    if (newINameable is IStaticPositionable positionable)
                    {
                        if (positionable is PositionedObject positionedObject && positionedObject.Parent != null)
                        {
                            positionedObject.RelativeX = x;
                            positionedObject.RelativeY = y;
                        }
                        else
                        {
                            positionable.X = x;
                            positionable.Y = y;
                        }
                    }

                    var addToSelection = i != 0;

                    EditingManager.Self.Select(newNos, addToExistingSelection: addToSelection);
                }

                await Managers.GlueCommands.Self.GluxCommands.SetVariableOnList(
                    variableAssignments,
                    currentElement,
                    performSaveAndGenerateCode: true, updateUi: true, recordUndo: true, echoToGame: true);

            }

            // It is possible for a paste to contain 0 items
            if (newNamedObjects.Count > 0)
            {
                await GlueState.Self.SetCurrentNamedObjectSaves(newNamedObjects, currentElement);
            }
        }

        private List<NosVariableAssignment> GetAfterPasteVariableAssignments(float? offsetX, float? offsetY, Vector3 positionOnPaste, List<NamedObjectSave> newNamedObjects)
        {
            List<NosVariableAssignment> variableAssignments = new List<NosVariableAssignment>();
            foreach (var newNos in newNamedObjects)
            {
                var x = positionOnPaste.X;
                var y = positionOnPaste.Y;

                if (offsetX != null)
                {
                    (float oldX, float oldY) = GetXY(newNos);
                    x = oldX + offsetX.Value;
                    y = oldY + offsetY.Value;
                }

                // place this where the cursor is - assuming the cursor is in the window
                variableAssignments.Add(new NosVariableAssignment
                {
                    NamedObjectSave = newNos,
                    VariableName = "X",
                    Value = positionOnPaste.X
                });
                variableAssignments.Add(new NosVariableAssignment
                {
                    NamedObjectSave = newNos,
                    VariableName = "Y",
                    Value = positionOnPaste.X
                });
            }

            return variableAssignments;
        }

        private void GetOffsetForPasting(IStaticPositionable itemGrabbed, List<NamedObjectSave> selectedNamedObjects, out float? offsetX, out float? offsetY)
        {
            offsetX = null;
            offsetY = null;
            NamedObjectSave matchingNos = null;
            if (itemGrabbed != null)
            {
                var itemGrabbedName = (itemGrabbed as INameable)?.Name;
                matchingNos = selectedNamedObjects.FirstOrDefault(item => item.InstanceName == itemGrabbedName);
            }
            if (matchingNos != null)
            {
                (float originalX, float originalY) = GetXY(matchingNos);

                var asPositionedObject = itemGrabbed as PositionedObject;

                if (asPositionedObject?.Parent == null)
                {
                    offsetX = itemGrabbed.X - originalX;
                    offsetY = itemGrabbed.Y - originalY;
                }
                else
                {
                    offsetX = asPositionedObject.RelativeX - originalX;
                    offsetY = asPositionedObject.RelativeY - originalY;
                }
            }
        }

        #endregion
    }
}
