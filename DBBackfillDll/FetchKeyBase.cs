﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace DBBackfill
{

    public class FetchKeyBase
    {
        //  Fetch Key Information
        //
        public string Name { get; protected set; } //  Name of this FetchKey

        //  Source Table info
        //
        public TableInfo FKeySrcTable { get; protected set; } // Reference to information on the source table 
        public List<string> FKeyColNames { get; protected set; }

        public List<TableColInfo> FKeyCopyCols = new List<TableColInfo>();  // Collection of data columns to be fetched and moved

        //  Extra properties needed for relational table fetch
        //
        public enum RLType
        {
            NoRelationSet = 0,
            InnerJoin,
            OuterJoin
        }
        public RLType RLConfigured { get; protected set; }

        //public bool RLRelationPresent { get; protected set; } // If true, then a foreign key relation is used on each data row fetch
        public TableInfo RLTable { get; protected set; }  // If not null, then a reference to a foreign/parent table
        public TableColInfo RLSrcTableCol { get; protected set; }  // If FKSrcTable not null, then reference to source table column
        public TableColInfo RLTableCol { get; protected set; } // If FKSrcTable not null, then reference to foreign table column


        //  Boolean properties
        //
        public bool FlgOrderBy = true;
        public bool FlgSelectByPartition = false; // If true, Fetch using partition number as first key

        //  Range start and stop limit lists
        //
        public List<object> StartKeyList { get; private set; }
        public List<object> EndKeyList { get; private set; }
        public List<object> RestartKeyList { get; private set; }

        //  Backfill 
        //
        public string TableHint { get; set; }


        public BackfillType FillType { get; set; }

        public string FillTypeName
        {
            get => FillType.ToString(); // Get the BackfillType name
            set => FillType = (BackfillType)Enum.Parse(typeof(BackfillType), value);
        }

        //  Additional WHERE clause specifications
        //
        public string AndWhere { get; set; }  // Raw TSQL contional used as an additional WHERE clause

        //  Restart positioning information 
        //
        public bool FlgRestart { get; protected set; } // Set true when initial restart check performed
        //{
        //    get;
        //    protected set;
        //}

        private int _restartPartition = 0;
        public int RestartPartition // Restart partition number
        {
            get => _restartPartition;
            set
            {
                _restartPartition = value;
                FlgRestart = true; // Set the restart flag accordingly
            }
        }

        public void AddRestartKey(object newKey)
        {
            RestartKeyList.Add(newKey);
            FlgRestart = true; // Mark as a restart
        }

        public void AddEndKey(object newKey)
        {
            EndKeyList.Add(newKey);
        }


        //  Constructed SQL commands
        //
        public string FetchKeyLimitsSql = ""; // Fetch th keys of the last row in the next fetch row group
        public string FetchBatchSql = ""; // Get the next batch of rows

        public string FetchLastSql = ""; // Get the last row of the batch


        //  Methods
        //
        public virtual void BuildFetchQuery(SqlCommand srcCmd, TableInfo srcTable, int batchSize,
                                            int partNumber, bool isFirstFetch, List<string> copyColNames, List<string> keyColNames, List<object> curKeys)
        {
            throw new ApplicationException("Not Implemented!");
        }

        public virtual List<object> FetchNextKeyList(DataRow lastDataRow)
        {
            throw new ApplicationException("Not Implemented!");
        }


        //
        //
        //  Relational data fetch methods
        //
        public void SetRelatedTable(TableInfo fkTable, string fkTableColName, string fkSrcTableColName, string rlTypeName = "InnerJoin")
        {
            SetRelatedTable(fkTable, fkTableColName, fkSrcTableColName, (RLType)Enum.Parse(typeof(RLType), rlTypeName, true));
        }

        public void SetRelatedTable(TableInfo fkTable, string fkTableColName, string fkSrcTableColName, RLType rlType = RLType.InnerJoin)
        {
            if ((fkTable != null) && !string.IsNullOrEmpty(fkTableColName) && !string.IsNullOrEmpty(fkSrcTableColName))
            {
                RLTable = fkTable; // Save reference o foreign table
                RLTableCol = RLTable[fkTableColName]; // Save column reference in foreign table
                RLSrcTableCol = FKeySrcTable[fkSrcTableColName]; ; // Save reference to column in source table
                RLConfigured = rlType;  // Save the relationship type
            }
            else
            {
                throw new ApplicationException("Improper call to SetRelatedTable");
            }
        }


        //
        //  Column transfer control methods
        //
        /// <summary>
        /// Add a calculated column to the backfill output table
        /// </summary>
        /// <param name="newCol">New column name</param>
        /// <param name="loadExpression">T-SQL expression</param>
        public void AddFetchCol(TableColInfo newCol, string loadExpression)
        {
            if (!FKeyCopyCols.Exists(dc => (dc.Name == newCol.Name)))
            {
                newCol.LoadExpression = loadExpression;  // Add the load expression needed to load data not in the src table
                FKeyCopyCols.Add(newCol);
            }
        }


        /// <summary>
        /// Modify the transferred contents of a source column when transferred to the output table
        /// </summary>
        /// <param name="colName">Existing source column name</param>
        /// <param name="loadExpression">T-SQL Expression</param>
        public void ModifyFetchCol(string colName, string loadExpression)
        {
            if (FKeyCopyCols.Exists(dc => (dc.Name == colName)))
            {
                FKeyCopyCols.Find(dc => dc.Name == colName).LoadExpression = loadExpression;  // Add the load expression needed to load data not in the src table
            }
            else
                throw new ApplicationException($"Cannot find existing source column:'{colName}'");
        }


        /// <summary>
        /// Ignore a source column and do not transfer it to the output table
        /// </summary>
        /// <param name="colName">Existing source table column name</param>
        public void IgnoreFetchCol(string colName)
        {
            if (FKeyCopyCols.Exists(dc => (dc.Name == colName)))
            {
                FKeyCopyCols.Remove(FKeyCopyCols.Find(dc => dc.Name == colName));  // Remove the column from the copy col list         
            }
            else
                throw new ApplicationException($"Cannot find existing source column:'{colName}'");
        }


        //
        //  Constructors
        //
        public FetchKeyBase(TableInfo srcTable, List<string> keyColNames)
        {
            //  Set up the source table information
            //
            FKeySrcTable = srcTable;
            FKeyColNames = keyColNames;
            if (keyColNames == null)
            {
                FKeyColNames = srcTable.Where(kc => kc.KeyOrdinal > 0).OrderBy(kc => kc.KeyOrdinal).Select(kc => kc.Name).ToList();
            }

            StartKeyList = new List<object>(); // Initialize the start/end key lists
            EndKeyList = new List<object>();

            FKeyCopyCols =
                FKeySrcTable.Columns
                            .Where(col => !col.Value.Ignore)
                            .Where(col => col.Value.IsCopyable)
                            .OrderBy(col => col.Value.ID)
                            .Select(sc => sc.Value).ToList();


            FlgRestart = false; // Assume no restart at this point
            RestartPartition = 1;  // Assume the first partition
            RestartKeyList = new List<object>(); // Clear out the restart keys list

            RLConfigured = RLType.NoRelationSet;  // No relationship set

            //  Check for any partitioning performance problems
            //
            RestartPartition = 1; // Default to the first partition
            FlgSelectByPartition = false; // Assume not partitioned
            if ((srcTable.IsPartitioned) && (srcTable.PtCol.ID == srcTable[keyColNames[0]].ID))
            {
                FlgSelectByPartition = true;
            }
        }

        public FetchKeyBase(TableInfo srcTable, string keyColNames)
            : this(srcTable, keyColNames?.Split(',').Select<string,string>(col => col.Trim(null)).ToList()) { }

    }

}
