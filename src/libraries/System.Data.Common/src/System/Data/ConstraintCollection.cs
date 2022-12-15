// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Collections;
using System.ComponentModel;
using System.Resources;

namespace System.Data
{
    /// <summary>
    /// Represents a collection of constraints for a <see cref='System.Data.DataTable'/>.
    /// </summary>
    [DefaultEvent(nameof(CollectionChanged))]
    [Editor("Microsoft.VSDesigner.Data.Design.ConstraintsCollectionEditor, Microsoft.VSDesigner, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a",
            "System.Drawing.Design.UITypeEditor, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
    public sealed class ConstraintCollection : InternalDataCollectionBase
    {
        private readonly DataTable _table;
        private readonly ArrayList _list = new ArrayList();
        private int _defaultNameIndex = 1;

        private CollectionChangeEventHandler? _onCollectionChanged;
        private Constraint[]? _delayLoadingConstraints;
        private bool _fLoadForeignKeyConstraintsOnly;

        /// <summary>
        /// ConstraintCollection constructor.  Used only by DataTable.
        /// </summary>
        internal ConstraintCollection(DataTable table)
        {
            Debug.Assert(table != null);
            _table = table;
        }

        /// <summary>
        /// Gets the list of objects contained by the collection.
        /// </summary>
        protected override ArrayList List => _list;

        /// <summary>
        /// Gets the <see cref='System.Data.Constraint'/>
        /// from the collection at the specified index.
        /// </summary>
        public Constraint this[int index]
        {
            get
            {
                if (index >= 0 && index < List.Count)
                {
                    return (Constraint)List[index]!;
                }
                throw ExceptionBuilder.ConstraintOutOfRange(index);
            }
        }

        /// <summary>
        /// The DataTable with which this ConstraintCollection is associated
        /// </summary>
        internal DataTable Table => _table;

        /// <summary>
        /// Gets the <see cref='System.Data.Constraint'/> from the collection with the specified name.
        /// </summary>
        public Constraint? this[string? name]
        {
            get
            {
                int index = InternalIndexOf(name);
                if (index == -2)
                {
                    throw ExceptionBuilder.CaseInsensitiveNameConflict(name!);
                }
                return (index < 0) ? null : (Constraint)List[index]!;
            }
        }

        /// <summary>
        /// Adds the constraint to the collection.
        /// </summary>
        public void Add(Constraint constraint) => Add(constraint, true);

        // To add foreign key constraint without adding any unique constraint for internal use. Main purpose : Binary Remoting
        internal void Add(Constraint constraint, bool addUniqueWhenAddingForeign)
        {
            if (constraint == null)
            {
                throw ExceptionBuilder.ArgumentNull(nameof(constraint));
            }

            // It is an error if we find an equivalent constraint already in collection
            if (FindConstraint(constraint) is Constraint matchedConstraint)
            {
                throw ExceptionBuilder.DuplicateConstraint(matchedConstraint.ConstraintName);
            }

            if (1 < _table.NestedParentRelations.Length)
            {
                if (!AutoGenerated(constraint))
                {
                    throw ExceptionBuilder.CantAddConstraintToMultipleNestedTable(_table.TableName);
                }
            }

            if (constraint is UniqueConstraint)
            {
                if (((UniqueConstraint)constraint)._bPrimaryKey)
                {
                    if (Table._primaryKey != null)
                    {
                        throw ExceptionBuilder.AddPrimaryKeyConstraint();
                    }
                }
                AddUniqueConstraint((UniqueConstraint)constraint);
            }
            else if (constraint is ForeignKeyConstraint)
            {
                ForeignKeyConstraint fk = (ForeignKeyConstraint)constraint;
                if (addUniqueWhenAddingForeign)
                {
                    UniqueConstraint? key = fk.RelatedTable.Constraints.FindKeyConstraint(fk.RelatedColumnsReference);
                    if (key == null)
                    {
                        if (constraint.ConstraintName.Length == 0)
                            constraint.ConstraintName = AssignName();
                        else
                            RegisterName(constraint.ConstraintName);

                        key = new UniqueConstraint(fk.RelatedColumnsReference);
                        fk.RelatedTable.Constraints.Add(key);
                    }
                }
                AddForeignKeyConstraint((ForeignKeyConstraint)constraint);
            }
            BaseAdd(constraint);
            ArrayAdd(constraint);
            OnCollectionChanged(new CollectionChangeEventArgs(CollectionChangeAction.Add, constraint));

            if (constraint is UniqueConstraint)
            {
                if (((UniqueConstraint)constraint)._bPrimaryKey)
                {
                    Table.PrimaryKey = ((UniqueConstraint)constraint).ColumnsReference;
                }
            }
        }

        /// <summary>
        /// Constructs a new <see cref='System.Data.UniqueConstraint'/> using the
        ///    specified array of <see cref='System.Data.DataColumn'/>
        ///    objects and adds it to the collection.
        /// </summary>
        public Constraint Add(string? name, DataColumn[] columns, bool primaryKey)
        {
            UniqueConstraint constraint = new UniqueConstraint(name, columns);
            Add(constraint);
            if (primaryKey)
                Table.PrimaryKey = columns;
            return constraint;
        }

        /// <summary>
        /// Constructs a new <see cref='System.Data.UniqueConstraint'/> using the
        ///    specified <see cref='System.Data.DataColumn'/> and adds it to the collection.
        /// </summary>
        public Constraint Add(string? name, DataColumn column, bool primaryKey)
        {
            UniqueConstraint constraint = new UniqueConstraint(name, column);
            Add(constraint);
            if (primaryKey)
                Table.PrimaryKey = constraint.ColumnsReference;
            return constraint;
        }

        /// <summary>
        /// Constructs a new <see cref='System.Data.ForeignKeyConstraint'/>
        /// with the
        /// specified parent and child
        /// columns and adds the constraint to the collection.
        /// </summary>
        public Constraint Add(string? name, DataColumn primaryKeyColumn, DataColumn foreignKeyColumn)
        {
            ForeignKeyConstraint constraint = new ForeignKeyConstraint(name, primaryKeyColumn, foreignKeyColumn);
            Add(constraint);
            return constraint;
        }

        /// <summary>
        /// Constructs a new <see cref='System.Data.ForeignKeyConstraint'/> with the specified parent columns and
        ///    child columns and adds the constraint to the collection.
        /// </summary>
        public Constraint Add(string? name, DataColumn[] primaryKeyColumns, DataColumn[] foreignKeyColumns)
        {
            ForeignKeyConstraint constraint = new ForeignKeyConstraint(name, primaryKeyColumns, foreignKeyColumns);
            Add(constraint);
            return constraint;
        }

        public void AddRange(Constraint[]? constraints)
        {
            if (_table.fInitInProgress)
            {
                _delayLoadingConstraints = constraints;
                _fLoadForeignKeyConstraintsOnly = false;
                return;
            }

            if (constraints != null)
            {
                foreach (Constraint constr in constraints)
                {
                    if (constr != null)
                    {
                        Add(constr);
                    }
                }
            }
        }


        private void AddUniqueConstraint(UniqueConstraint constraint)
        {
            DataColumn[] columns = constraint.ColumnsReference;

            for (int i = 0; i < columns.Length; i++)
            {
                if (columns[i].Table != _table)
                {
                    throw ExceptionBuilder.ConstraintForeignTable();
                }
            }
            constraint.ConstraintIndexInitialize();

            if (!constraint.CanEnableConstraint())
            {
                constraint.ConstraintIndexClear();
                throw ExceptionBuilder.UniqueConstraintViolation();
            }
        }

        private void AddForeignKeyConstraint(ForeignKeyConstraint constraint)
        {
            if (!constraint.CanEnableConstraint())
            {
                throw ExceptionBuilder.ConstraintParentValues();
            }
            constraint.CheckCanAddToCollection(this);
        }

        private bool AutoGenerated(Constraint constraint)
        {
            ForeignKeyConstraint? fk = (constraint as ForeignKeyConstraint);
            if (null != fk)
            {
                return XmlTreeGen.AutoGenerated(fk, false);
            }
            else
            {
                UniqueConstraint unique = (UniqueConstraint)constraint;
                return XmlTreeGen.AutoGenerated(unique);
            }
        }

        /// <summary>
        /// Occurs when the <see cref='System.Data.ConstraintCollection'/> is changed through additions or
        ///    removals.
        /// </summary>
        public event CollectionChangeEventHandler? CollectionChanged
        {
            add
            {
                _onCollectionChanged += value;
            }
            remove
            {
                _onCollectionChanged -= value;
            }
        }

        /// <summary>
        ///  Adds the constraint to the constraints array.
        /// </summary>
        private void ArrayAdd(Constraint constraint)
        {
            Debug.Assert(constraint != null, "Attempt to add null constraint to constraint array");
            List.Add(constraint);
        }

        private void ArrayRemove(Constraint constraint)
        {
            List.Remove(constraint);
        }

        /// <summary>
        /// Creates a new default name.
        /// </summary>
        internal string AssignName()
        {
            string newName = MakeName(_defaultNameIndex);
            _defaultNameIndex++;
            return newName;
        }

        /// <summary>
        /// Does verification on the constraint and it's name.
        /// An ArgumentNullException is thrown if this constraint is null.  An ArgumentException is thrown if this constraint
        /// already belongs to this collection, belongs to another collection.
        /// A DuplicateNameException is thrown if this collection already has a constraint with the same
        /// name (case insensitive).
        /// </summary>
        private void BaseAdd(Constraint constraint)
        {
            if (constraint == null)
                throw ExceptionBuilder.ArgumentNull(nameof(constraint));

            if (constraint.ConstraintName.Length == 0)
                constraint.ConstraintName = AssignName();
            else
                RegisterName(constraint.ConstraintName);

            constraint.InCollection = true;
        }

        /// <summary>
        /// BaseGroupSwitch will intelligently remove and add tables from the collection.
        /// </summary>
        private void BaseGroupSwitch(Constraint[] oldArray, int oldLength, Constraint[] newArray, int newLength)
        {
            // We're doing a smart diff of oldArray and newArray to find out what
            // should be removed.  We'll pass through oldArray and see if it exists
            // in newArray, and if not, do remove work.  newBase is an opt. in case
            // the arrays have similar prefixes.
            int newBase = 0;
            for (int oldCur = 0; oldCur < oldLength; oldCur++)
            {
                bool found = false;
                for (int newCur = newBase; newCur < newLength; newCur++)
                {
                    if (oldArray[oldCur] == newArray[newCur])
                    {
                        if (newBase == newCur)
                        {
                            newBase++;
                        }
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    // This means it's in oldArray and not newArray.  Remove it.
                    BaseRemove(oldArray[oldCur]);
                    List.Remove(oldArray[oldCur]);
                }
            }

            // Now, let's pass through news and those that don't belong, add them.
            for (int newCur = 0; newCur < newLength; newCur++)
            {
                if (!newArray[newCur].InCollection)
                    BaseAdd(newArray[newCur]);
                List.Add(newArray[newCur]);
            }
        }

        /// <summary>
        /// Does verification on the constraint and it's name.
        /// An ArgumentNullException is thrown if this constraint is null.  An ArgumentException is thrown
        /// if this constraint doesn't belong to this collection or if this constraint is part of a relationship.
        /// </summary>
        private void BaseRemove(Constraint constraint)
        {
            if (constraint == null)
            {
                throw ExceptionBuilder.ArgumentNull(nameof(constraint));
            }
            if (constraint.Table != _table)
            {
                throw ExceptionBuilder.ConstraintRemoveFailed();
            }

            UnregisterName(constraint.ConstraintName);
            constraint.InCollection = false;

            if (constraint is UniqueConstraint)
            {
                for (int i = 0; i < Table.ChildRelations.Count; i++)
                {
                    DataRelation rel = Table.ChildRelations[i];
                    if (rel.ParentKeyConstraint == constraint)
                        rel.SetParentKeyConstraint(null);
                }
                ((UniqueConstraint)constraint).ConstraintIndexClear();
            }
            else if (constraint is ForeignKeyConstraint)
            {
                for (int i = 0; i < Table.ParentRelations.Count; i++)
                {
                    DataRelation rel = Table.ParentRelations[i];
                    if (rel.ChildKeyConstraint == constraint)
                        rel.SetChildKeyConstraint(null);
                }
            }
        }

        /// <summary>
        /// Indicates if a <see cref='System.Data.Constraint'/> can be removed.
        /// </summary>
        // PUBLIC because called by design-time... need to consider this.
        public bool CanRemove(Constraint constraint)
        {
            return CanRemove(constraint, fThrowException: false);
        }

        internal bool CanRemove(Constraint constraint, bool fThrowException)
        {
            return constraint.CanBeRemovedFromCollection(this, fThrowException);
        }

        /// <summary>
        /// Clears the collection of any <see cref='System.Data.Constraint'/>
        /// objects.
        /// </summary>
        public void Clear()
        {
            _table.PrimaryKey = null;

            for (int i = 0; i < _table.ParentRelations.Count; i++)
            {
                _table.ParentRelations[i].SetChildKeyConstraint(null);
            }
            for (int i = 0; i < _table.ChildRelations.Count; i++)
            {
                _table.ChildRelations[i].SetParentKeyConstraint(null);
            }

            if (_table.fInitInProgress && _delayLoadingConstraints != null)
            {
                _delayLoadingConstraints = null;
                _fLoadForeignKeyConstraintsOnly = false;
            }

            int oldLength = List.Count;

            Constraint[] constraints = new Constraint[List.Count];
            List.CopyTo(constraints, 0);
            try
            {
                // this will smartly add and remove the appropriate tables.
                BaseGroupSwitch(constraints, oldLength, Array.Empty<Constraint>(), 0);
            }
            catch (Exception e) when (Common.ADP.IsCatchableOrSecurityExceptionType(e))
            {
                // something messed up.  restore to original state.
                BaseGroupSwitch(Array.Empty<Constraint>(), 0, constraints, oldLength);
                List.Clear();
                for (int i = 0; i < oldLength; i++)
                {
                    List.Add(constraints[i]);
                }
                throw;
            }

            List.Clear();
            OnCollectionChanged(s_refreshEventArgs);
        }

        /// <summary>
        /// Indicates whether the <see cref='System.Data.Constraint'/>, specified by name, exists in the collection.
        /// </summary>
        public bool Contains(string? name)
        {
            return (InternalIndexOf(name) >= 0);
        }

        internal bool Contains(string? name, bool caseSensitive)
        {
            if (!caseSensitive)
                return Contains(name);
            int index = InternalIndexOf(name);
            if (index < 0)
                return false;
            return (name == ((Constraint)List[index]!).ConstraintName);
        }

        public void CopyTo(Constraint[] array, int index)
        {
            if (array == null)
                throw ExceptionBuilder.ArgumentNull(nameof(array));
            if (index < 0)
                throw ExceptionBuilder.ArgumentOutOfRange(nameof(index));
            if (array.Length - index < _list.Count)
                throw ExceptionBuilder.InvalidOffsetLength();
            for (int i = 0; i < _list.Count; ++i)
            {
                array[index + i] = (Constraint)_list[i]!;
            }
        }

        /// <summary>
        /// Returns a matching constraint object.
        /// </summary>
        internal Constraint? FindConstraint(Constraint? constraint)
        {
            int constraintCount = List.Count;
            for (int i = 0; i < constraintCount; i++)
            {
                if (((Constraint)List[i]!).Equals(constraint))
                    return (Constraint)List[i]!;
            }
            return null;
        }

        /// <summary>
        /// Returns a matching constraint object.
        /// </summary>
        internal UniqueConstraint? FindKeyConstraint(DataColumn[] columns)
        {
            int constraintCount = List.Count;
            for (int i = 0; i < constraintCount; i++)
            {
                UniqueConstraint? constraint = (List[i] as UniqueConstraint);
                if ((null != constraint) && CompareArrays(constraint.Key.ColumnsReference, columns))
                {
                    return constraint;
                }
            }
            return null;
        }

        /// <summary>
        /// Returns a matching constraint object.
        /// </summary>
        internal UniqueConstraint? FindKeyConstraint(DataColumn column)
        {
            int constraintCount = List.Count;
            for (int i = 0; i < constraintCount; i++)
            {
                UniqueConstraint? constraint = (List[i] as UniqueConstraint);
                if ((null != constraint) && (constraint.Key.ColumnsReference.Length == 1) && (constraint.Key.ColumnsReference[0] == column))
                    return constraint;
            }
            return null;
        }

        /// <summary>
        /// Returns a matching constraint object.
        /// </summary>
        internal ForeignKeyConstraint? FindForeignKeyConstraint(DataColumn[] parentColumns, DataColumn[] childColumns)
        {
            int constraintCount = List.Count;
            for (int i = 0; i < constraintCount; i++)
            {
                ForeignKeyConstraint? constraint = (List[i] as ForeignKeyConstraint);
                if ((null != constraint) &&
                    CompareArrays(constraint.ParentKey.ColumnsReference, parentColumns) &&
                    CompareArrays(constraint.ChildKey.ColumnsReference, childColumns))
                    return constraint;
            }
            return null;
        }

        private static bool CompareArrays(DataColumn[] a1, DataColumn[] a2)
        {
            Debug.Assert(a1 != null && a2 != null, "Invalid Arguments");
            if (a1.Length != a2.Length)
                return false;

            int i, j;
            for (i = 0; i < a1.Length; i++)
            {
                bool check = false;
                for (j = 0; j < a2.Length; j++)
                {
                    if (a1[i] == a2[j])
                    {
                        check = true;
                        break;
                    }
                }
                if (!check)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns the index of the specified <see cref='System.Data.Constraint'/> .
        /// </summary>
        public int IndexOf(Constraint? constraint)
        {
            if (null != constraint)
            {
                int count = Count;
                for (int i = 0; i < count; ++i)
                {
                    if (constraint == (Constraint)List[i]!)
                        return i;
                }
                // didn't find the constraint
            }
            return -1;
        }

        /// <summary>
        /// Returns the index of the <see cref='System.Data.Constraint'/>, specified by name.
        /// </summary>
        public int IndexOf(string? constraintName)
        {
            int index = InternalIndexOf(constraintName);
            return (index < 0) ? -1 : index;
        }

        // Return value:
        //      >= 0: find the match
        //        -1: No match
        //        -2: At least two matches with different cases
        internal int InternalIndexOf(string? constraintName)
        {
            int cachedI = -1;
            if ((null != constraintName) && (0 < constraintName.Length))
            {
                int constraintCount = List.Count;
                for (int i = 0; i < constraintCount; i++)
                {
                    Constraint constraint = (Constraint)List[i]!;
                    int result = NamesEqual(constraint.ConstraintName, constraintName, false, _table.Locale);
                    if (result == 1)
                        return i;

                    if (result == -1)
                        cachedI = (cachedI == -1) ? i : -2;
                }
            }
            return cachedI;
        }

        /// <summary>
        /// Makes a default name with the given index.  e.g. Constraint1, Constraint2, ... Constrainti
        /// </summary>
        private string MakeName(int index)
        {
            if (1 == index)
            {
                return "Constraint1";
            }
            return "Constraint" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Raises the <see cref='System.Data.ConstraintCollection.CollectionChanged'/> event.
        /// </summary>
        private void OnCollectionChanged(CollectionChangeEventArgs ccevent)
        {
            _onCollectionChanged?.Invoke(this, ccevent);
        }

        /// <summary>
        /// Registers this name as being used in the collection.  Will throw an ArgumentException
        /// if the name is already being used.  Called by Add, All property, and Constraint.ConstraintName property.
        /// if the name is equivalent to the next default name to hand out, we increment our defaultNameIndex.
        /// </summary>
        internal void RegisterName(string name)
        {
            Debug.Assert(name != null);

            int constraintCount = List.Count;
            for (int i = 0; i < constraintCount; i++)
            {
                if (NamesEqual(name, ((Constraint)List[i]!).ConstraintName, true, _table.Locale) != 0)
                {
                    throw ExceptionBuilder.DuplicateConstraintName(((Constraint)List[i]!).ConstraintName);
                }
            }
            if (NamesEqual(name, MakeName(_defaultNameIndex), true, _table.Locale) != 0)
            {
                _defaultNameIndex++;
            }
        }

        /// <summary>
        /// Removes the specified <see cref='System.Data.Constraint'/> from the collection.
        /// </summary>
        public void Remove(Constraint constraint)
        {
            if (constraint == null)
                throw ExceptionBuilder.ArgumentNull(nameof(constraint));

            // this will throw an exception if it can't be removed, otherwise indicates
            // whether we need to remove it from the collection.
            if (CanRemove(constraint, true))
            {
                // constraint can be removed
                BaseRemove(constraint);
                ArrayRemove(constraint);
                if (constraint is UniqueConstraint && ((UniqueConstraint)constraint).IsPrimaryKey)
                {
                    Table.PrimaryKey = null;
                }

                OnCollectionChanged(new CollectionChangeEventArgs(CollectionChangeAction.Remove, constraint));
            }
        }

        /// <summary>
        /// Removes the constraint at the specified index from the collection.
        /// </summary>
        public void RemoveAt(int index)
        {
            Constraint c = this[index];
            if (c == null)
                throw ExceptionBuilder.ConstraintOutOfRange(index);
            Remove(c);
        }

        /// <summary>
        /// Removes the constraint, specified by name, from the collection.
        /// </summary>
        public void Remove(string name)
        {
            Constraint? c = this[name];
            if (c == null)
                throw ExceptionBuilder.ConstraintNotInTheTable(name);
            Remove(c);
        }

        /// <summary>
        /// Unregisters this name as no longer being used in the collection.  Called by Remove, All property, and
        /// Constraint.ConstraintName property.  If the name is equivalent to the last proposed default name, we walk backwards
        /// to find the next proper default name to use.
        /// </summary>
        internal void UnregisterName(string name)
        {
            if (NamesEqual(name, MakeName(_defaultNameIndex - 1), true, _table.Locale) != 0)
            {
                do
                {
                    _defaultNameIndex--;
                } while (_defaultNameIndex > 1 &&
                         !Contains(MakeName(_defaultNameIndex - 1)));
            }
        }

        internal void FinishInitConstraints()
        {
            if (_delayLoadingConstraints == null)
                return;

            int colCount;
            DataColumn[] parents, childs;
            for (int i = 0; i < _delayLoadingConstraints.Length; i++)
            {
                if (_delayLoadingConstraints[i] is UniqueConstraint)
                {
                    if (_fLoadForeignKeyConstraintsOnly)
                        continue;

                    UniqueConstraint constr = (UniqueConstraint)_delayLoadingConstraints[i];
                    if (constr._columnNames == null)
                    {
                        Add(constr);
                        continue;
                    }
                    colCount = constr._columnNames.Length;
                    parents = new DataColumn[colCount];
                    for (int j = 0; j < colCount; j++)
                        parents[j] = _table.Columns[constr._columnNames[j]]!;
                    if (constr._bPrimaryKey)
                    {
                        if (_table._primaryKey != null)
                        {
                            throw ExceptionBuilder.AddPrimaryKeyConstraint();
                        }
                        else
                        {
                            Add(constr.ConstraintName, parents, true);
                        }
                        continue;
                    }
                    UniqueConstraint newConstraint = new UniqueConstraint(constr._constraintName, parents);
                    if (FindConstraint(newConstraint) == null)
                        Add(newConstraint);
                }
                else
                {
                    ForeignKeyConstraint constr = (ForeignKeyConstraint)_delayLoadingConstraints[i];
                    if (constr._parentColumnNames == null || constr._childColumnNames == null)
                    {
                        Add(constr);
                        continue;
                    }

                    Debug.Assert(constr._parentTableName != null);

                    if (_table.DataSet == null)
                    {
                        _fLoadForeignKeyConstraintsOnly = true;
                        continue;
                    }

                    colCount = constr._parentColumnNames.Length;
                    parents = new DataColumn[colCount];
                    childs = new DataColumn[colCount];
                    for (int j = 0; j < colCount; j++)
                    {
                        if (constr._parentTableNamespace == null)
                            parents[j] = _table.DataSet.Tables[constr._parentTableName]!.Columns[constr._parentColumnNames[j]]!;
                        else
                            parents[j] = _table.DataSet.Tables[constr._parentTableName, constr._parentTableNamespace]!.Columns[constr._parentColumnNames[j]]!;
                        childs[j] = _table.Columns[constr._childColumnNames[j]]!;
                    }
                    ForeignKeyConstraint newConstraint = new ForeignKeyConstraint(constr._constraintName, parents, childs);
                    newConstraint.AcceptRejectRule = constr._acceptRejectRule;
                    newConstraint.DeleteRule = constr._deleteRule;
                    newConstraint.UpdateRule = constr._updateRule;
                    Add(newConstraint);
                }
            }

            if (!_fLoadForeignKeyConstraintsOnly)
                _delayLoadingConstraints = null;
        }
    }
}
