﻿using DICOMcloud;
using DICOMcloud.DataAccess.Database.Commands;
using DICOMcloud.DataAccess.Database.Schema;
using DICOMcloud.DataAccess.Matching;
using DICOMcloud.Extensions;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using fo = Dicom;

namespace DICOMcloud.DataAccess.Database
{
    public abstract class ObjectArchieveDataAdapter
    {
        public ObjectArchieveDataAdapter ( DbSchemaProvider schemaProvider )
        {
            SchemaProvider = schemaProvider ;
        }

        public DbSchemaProvider SchemaProvider
        {
            get;
            protected set;
        }

        public IDbCommand CreateCommand ( string commandText )
        {
            var command = CreateCommand ( );

            SetConnectionIfNull ( command );

            command.CommandText = commandText ;

            return command;
        }

        public virtual IDbCommand CreateSelectCommand 
        ( 
            string sourceTable, 
            IEnumerable<IMatchingCondition> conditions, 
            IQueryOptions options,
            out string[] tables 
        )
        {
            var queryBuilder  = BuildQuery    ( conditions, options, sourceTable          ) ;
            var SelectCommand = CreateCommand ( queryBuilder.GetQueryText ( sourceTable ) ) ;


            tables = queryBuilder.GetQueryResultTables ( ).ToArray ( ) ;

            return SelectCommand ;
        }

        public virtual IDataAdapterCommand<IEnumerable<fo.DicomDataset>> CreateSelectCommand 
        ( 
            string sourceTable, 
            IEnumerable<IMatchingCondition> conditions, 
            IQueryOptions options,
            IQueryResponseBuilder responseBuilder
        )
        {
            var queryBuilder  = BuildQuery ( conditions, options, sourceTable ) ;
            

            var selectCommand = new DicomDsQueryCommand ( CreateCommand ( queryBuilder.GetQueryText ( sourceTable ) ), queryBuilder, responseBuilder ) ;

            return selectCommand ;
        }

        public virtual IDataAdapterCommand<long> CreateSelectStudyKeyCommand ( IStudyId study )
        {
            TableKey                   studyTable   = SchemaProvider.GetTableInfo ( StorageDbSchemaProvider.StudyTableName );
            QueryBuilder queryBuilder = CreateQueryBuilder ( ) ;
            SingleValueMatching        uidMatching  = new SingleValueMatching ( ) ;


            queryBuilder.ProcessColumn ( studyTable, studyTable.ModelKeyColumns [0], uidMatching, new string[] { study.StudyInstanceUID } );
                        
            return new SingleResultQueryCommand<long> ( CreateCommand ( queryBuilder.GetQueryText ( studyTable ) ), 
                                                        studyTable.Name,
                                                        studyTable.KeyColumn.Name ) ;
        }

        public virtual IDataAdapterCommand<long> CreateSelectSeriesKeyCommand ( ISeriesId series )
        {
            TableKey                   studyTable   = SchemaProvider.GetTableInfo ( StorageDbSchemaProvider.StudyTableName );
            TableKey                   seriesTable  = SchemaProvider.GetTableInfo ( StorageDbSchemaProvider.SeriesTableName );
            QueryBuilder queryBuilder = CreateQueryBuilder ( ) ;
            SingleValueMatching        uidMatching  = new SingleValueMatching ( ) ;


            queryBuilder.ProcessColumn ( seriesTable, studyTable.ModelKeyColumns  [0], uidMatching, new string[] { series.StudyInstanceUID } );
            queryBuilder.ProcessColumn ( seriesTable, seriesTable.ModelKeyColumns [0], uidMatching, new string[] { series.SeriesInstanceUID } );
            
            return new SingleResultQueryCommand<long> ( CreateCommand ( queryBuilder.GetQueryText ( seriesTable ) ),
                                                        seriesTable.Name,
                                                        seriesTable.KeyColumn.Name ) ;
        }

        public virtual IDataAdapterCommand<long> CreateSelectInstanceKeyCommand ( IObjectId instance ) 
        {
            QueryBuilder queryBuilder   = CreateQueryBuilder ( ) ;
            TableKey                   sourceTable    = SchemaProvider.GetTableInfo ( StorageDbSchemaProvider.ObjectInstanceTableName ) ;
            SingleValueMatching        sopUIDMatching = new SingleValueMatching ( ) ;


            queryBuilder.ProcessColumn ( sourceTable, sourceTable.ModelKeyColumns[0], sopUIDMatching, new string[] { instance.SOPInstanceUID } );
            
            
            return new SingleResultQueryCommand<long> ( CreateCommand ( queryBuilder.GetQueryText ( sourceTable ) ),
                                                        sourceTable.Name, 
                                                        sourceTable.KeyColumn.Name ) ;                       
        }

        public virtual IDbCommand CreateInsertCommand 
        ( 
            IEnumerable<IDicomDataParameter> conditions,
            InstanceMetadata data = null
        )
        {
            IDbCommand insertCommand = CreateCommand ( ) ;

            BuildInsert ( conditions, data, insertCommand ) ;

            SetConnectionIfNull ( insertCommand ) ;
            
            return insertCommand ;
        
        }
        
        public virtual IDataAdapterCommand<int> CreateDeleteStudyCommand ( long studyKey )
        {
            return new ExecuteNonQueryCommand ( CreateCommand ( new SqlDeleteStatments ( )
                                                              .GetDeleteStudyCommandText ( studyKey ) ) ) ;
        }
        
        public virtual IDataAdapterCommand<int> CreateDeleteSeriesCommand ( long seriesKey )
        {
            return new ExecuteNonQueryCommand ( CreateCommand ( new SqlDeleteStatments ( )
                                                               .GetDeleteSeriesCommandText ( seriesKey ) ) ) ;
        }
        
        public virtual IDataAdapterCommand<int> CreateDeleteInstancCommand ( long instanceKey )
        {
            return new ExecuteNonQueryCommand ( CreateCommand ( new SqlDeleteStatments ( ).
                                                               GetDeleteInstanceCommandText ( instanceKey ) ) ) ;
        }

        public IDataAdapterCommand<int> CreateUpdateMetadataCommand ( IObjectId objectId, InstanceMetadata data )
        {
            IDbCommand insertCommand = CreateCommand ( ) ;
            var instance             = objectId ;
            

            insertCommand = CreateCommand ( ) ;

            insertCommand.CommandText = string.Format ( @"
UPDATE {0} SET {2}=@{2}, {3}=@{3} WHERE {1}=@{1}

IF @@ROWCOUNT = 0
   INSERT INTO {0} ({2}, {3}) VALUES (@{2}, @{3})
", 
StorageDbSchemaProvider.MetadataTable.TableName, 
StorageDbSchemaProvider.MetadataTable.SopInstanceColumn, 
StorageDbSchemaProvider.MetadataTable.MetadataColumn,
StorageDbSchemaProvider.MetadataTable.OwnerColumn ) ;

             var sopParam   = CreateParameter ( "@" + StorageDbSchemaProvider.MetadataTable.SopInstanceColumn, instance.SOPInstanceUID ) ;
             var metaParam  = CreateParameter ( "@" + StorageDbSchemaProvider.MetadataTable.MetadataColumn, data.ToJson ( ) ) ;
             var ownerParam = CreateParameter ( "@" + StorageDbSchemaProvider.MetadataTable.OwnerColumn, data.Owner ) ;
            
            insertCommand.Parameters.Add ( sopParam ) ;
            insertCommand.Parameters.Add ( metaParam ) ;
            insertCommand.Parameters.Add ( ownerParam ) ;

            SetConnectionIfNull ( insertCommand ) ;        
            
            return new ExecuteNonQueryCommand ( insertCommand ) ; 
        }

        public ResultSetQueryCommand<InstanceMetadata> CreateGetMetadataCommand ( IStudyId study )
        {
            TableKey                   studyTable     = SchemaProvider.GetTableInfo ( StorageDbSchemaProvider.StudyTableName );
            TableKey                   sourceTable    = SchemaProvider.GetTableInfo ( StorageDbSchemaProvider.ObjectInstanceTableName );
            QueryBuilder queryBuilder   = CreateQueryBuilder          ( );
            SingleValueMatching        uidMatching    = new SingleValueMatching     ( ) ;
            ColumnInfo                 studyColumne   = studyTable.ModelKeyColumns[0] ;
            ColumnInfo                 metaDataColumn = SchemaProvider.GetColumn ( sourceTable.Name,
                                                                                   StorageDbSchemaProvider.MetadataTable.MetadataColumn ) ;


            queryBuilder.ProcessColumn ( sourceTable, studyColumne, uidMatching, new string[] { study.StudyInstanceUID } );

            queryBuilder.ProcessColumn ( sourceTable, metaDataColumn, null, null );

            return new ResultSetQueryCommand<InstanceMetadata> ( CreateCommand ( queryBuilder.GetQueryText ( sourceTable ) ), 
                                                      sourceTable, 
                                                      new string [] { metaDataColumn.ToString ( ) },
                                                      CreateMetadata );
        }

        public ResultSetQueryCommand<InstanceMetadata> CreateGetMetadataCommand ( ISeriesId series )
        {
            TableKey                   studyTable   = SchemaProvider.GetTableInfo ( StorageDbSchemaProvider.StudyTableName );
            TableKey                   seriesTable  = SchemaProvider.GetTableInfo ( StorageDbSchemaProvider.SeriesTableName );
            TableKey                   sourceTable  = SchemaProvider.GetTableInfo ( StorageDbSchemaProvider.ObjectInstanceTableName );
            QueryBuilder queryBuilder = CreateQueryBuilder          ( );
            SingleValueMatching        uidMatching  = new SingleValueMatching     ( ) ;
            ColumnInfo                 metadataColumn = SchemaProvider.GetColumn ( sourceTable.Name,
                                                                                   StorageDbSchemaProvider.MetadataTable.MetadataColumn ) ;

            queryBuilder.ProcessColumn ( sourceTable, seriesTable.ModelKeyColumns[0], uidMatching, new string[] { series.SeriesInstanceUID } );
            queryBuilder.ProcessColumn ( sourceTable, studyTable.ModelKeyColumns[0],  uidMatching, new string[] { series.StudyInstanceUID  } );
            queryBuilder.ProcessColumn ( sourceTable, metadataColumn  );

            return new ResultSetQueryCommand<InstanceMetadata> ( CreateCommand ( queryBuilder.GetQueryText ( sourceTable ) ),
                                                                 sourceTable,
                                                                 new string [] { metadataColumn.ToString ( ) },
                                                                 CreateMetadata ) ;
        }

        public SingleResultQueryCommand<InstanceMetadata> CreateGetMetadataCommand ( IObjectId instance )
        {
            TableKey                   studyTable    = SchemaProvider.GetTableInfo ( StorageDbSchemaProvider.StudyTableName );
            TableKey                   seriesTable   = SchemaProvider.GetTableInfo ( StorageDbSchemaProvider.SeriesTableName );
            TableKey                   instanceTable = SchemaProvider.GetTableInfo ( StorageDbSchemaProvider.ObjectInstanceTableName );
            QueryBuilder queryBuilder  = CreateQueryBuilder          ( );
            SingleValueMatching        uidMatching   = new SingleValueMatching     ( ) ;
            ColumnInfo                 metadataColumn = SchemaProvider.GetColumn ( instanceTable.Name,
                                                                                   StorageDbSchemaProvider.MetadataTable.MetadataColumn ) ;

            queryBuilder.ProcessColumn ( instanceTable, seriesTable.ModelKeyColumns[0], uidMatching, new string[] { instance.SeriesInstanceUID } );
            queryBuilder.ProcessColumn ( instanceTable, studyTable.ModelKeyColumns[0],  uidMatching, new string[] { instance.StudyInstanceUID  } );
            queryBuilder.ProcessColumn ( instanceTable, instanceTable.ModelKeyColumns[0], uidMatching, new string[] { instance.SOPInstanceUID } );
            queryBuilder.ProcessColumn ( instanceTable, metadataColumn  );

            return new SingleResultQueryCommand<InstanceMetadata> ( CreateCommand ( queryBuilder.GetQueryText ( instanceTable ) ),
                                                                    instanceTable,
                                                                    metadataColumn.ToString ( ),
                                                                    CreateMetadata ) ;

            //IDbCommand command  = CreateCommand ( ) ;
            //var        sopParam = CreateParameter ( "@" + DB.Schema.StorageDbSchemaProvider.MetadataTable.SopInstanceColumn, instance.SOPInstanceUID ) ;
            
             
            // command.CommandText = string.Format ( "SELECT {0} FROM {1} WHERE {2}=@{2}", 
            //                                      DB.Schema.StorageDbSchemaProvider.MetadataTable.MetadataColumn,
            //                                      DB.Schema.StorageDbSchemaProvider.MetadataTable.TableName,
            //                                      DB.Schema.StorageDbSchemaProvider.MetadataTable.SopInstanceColumn ) ;

            //command.Parameters.Add ( sopParam );

            //SetConnectionIfNull ( command ) ;
            
            //return command ;
        }

        public long ReadStudyKey ( IDataReader reader )
        {
            return (long) reader[SchemaProvider.GetTableInfo ( StorageDbSchemaProvider.StudyTableName ).KeyColumn.Name] ;
        }

        public long ReadSeriesKey ( IDataReader reader )
        {
            return (long) reader[SchemaProvider.GetTableInfo ( StorageDbSchemaProvider.SeriesTableName ).KeyColumn.Name] ;
        }

        public long ReadInstanceKey ( IDataReader reader )
        {
            return (long) reader[SchemaProvider.GetTableInfo ( StorageDbSchemaProvider.ObjectInstanceTableName ).KeyColumn.Name] ;
        }

        public string ReadInstanceMetadata ( IDataReader reader )
        {
            return reader[StorageDbSchemaProvider.MetadataTable.MetadataColumn] as string ;
        }

        public abstract IDbConnection CreateConnection ( ) ;
        

        protected abstract IDbCommand  CreateCommand ( ) ;

        protected abstract IDbDataParameter CreateParameter ( string columnName, object Value ) ;

        protected virtual QueryBuilder BuildQuery 
        ( 
            IEnumerable<IMatchingCondition> conditions, 
            IQueryOptions options,
            string queryLevel 
        )
        {
            QueryBuilder queryBuilder = CreateQueryBuilder ( ) ;
            TableKey                   sourceTable  = SchemaProvider.GetTableInfo ( SchemaProvider.GetQueryTable ( queryLevel ) ) ;


            if ( sourceTable == null )
            { 
                throw new ArgumentException ( "querylevel not supported" ) ;
            }

            if ( null != conditions )
            {
                foreach ( var condition in conditions )
                {
                    if ( condition.VR == fo.DicomVR.PN )
                    { 
                        List<PersonNameData> pnValues = new List<PersonNameData> ( ) ;

                         
                        pnValues = condition.GetPNValues ( ) ;
                        
                        foreach ( var values in pnValues )
                        {
                            int          index = -1 ;
                            string[]     stringValues = values.ToArray ( ) ;
                            List<string> pnConditions = new List<string> ( ) ;

                            foreach ( var column in SchemaProvider.GetColumnInfo ( condition.KeyTag ) )
                            { 
                                var columnValues = new string [] { stringValues[++index]} ;
                                
                                queryBuilder.ProcessColumn ( sourceTable, column, condition, columnValues ) ;
                            }
                        }
                    }
                    else
                    { 
                        IList<string> columnValues = GetValues ( condition )  ;

                        foreach ( var column in SchemaProvider.GetColumnInfo ( condition.KeyTag ) )
                        { 
                            queryBuilder.ProcessColumn ( sourceTable, column, condition, columnValues ) ;
                        }
                    }
                }
            }
        
            return queryBuilder ;
        }

        protected virtual void BuildInsert 
        ( 
            IEnumerable<IDicomDataParameter> conditions, 
            InstanceMetadata data, 
            IDbCommand insertCommand 
        )
        {
            if ( null == conditions ) throw new ArgumentNullException ( ) ;

            var stroageBuilder = CreateStorageBuilder ( ) ;
            
            FillParameters ( conditions, data, insertCommand, stroageBuilder ) ;
            
            insertCommand.CommandText = stroageBuilder.GetInsertText ( ) ;
        }
        
        protected virtual void SetConnectionIfNull ( IDbCommand command )
        {
            if (command !=null && command.Connection == null)
            {
                command.Connection = CreateConnection ( ) ;
            }
        }

        protected virtual void FillParameters
        (
            IEnumerable<IDicomDataParameter> dicomParameters,
            InstanceMetadata data, 
            IDbCommand insertCommad,
            ObjectArchieveStorageBuilder stroageBuilder
        )
        {
            foreach ( var dicomParam in dicomParameters )
            {
                if ( dicomParam.VR == fo.DicomVR.PN )
                { 
                    List<PersonNameData> pnValues ;

                         
                    pnValues = dicomParam.GetPNValues ( ) ;
                        
                    foreach ( var values in pnValues )
                    {
                        string[] stringValues = values.ToArray ( ) ;
                        int index = -1 ;
                        List<string> pnConditions = new List<string> ( ) ;

                        foreach ( var column in SchemaProvider.GetColumnInfo ( dicomParam.KeyTag ) )
                        { 
                            column.Values = new string [] { stringValues[++index]} ;
                                
                            stroageBuilder.ProcessColumn ( column, insertCommad, CreateParameter ) ;
                        }
                    }
                    
                    continue ;
                }

                
                foreach ( var column in SchemaProvider.GetColumnInfo ( dicomParam.KeyTag ) )
                { 
                    column.Values = GetValues ( dicomParam ) ;
                        
                    stroageBuilder.ProcessColumn ( column, insertCommad, CreateParameter ) ;
                }
            }
        }

        protected virtual QueryBuilder CreateQueryBuilder ( ) 
        {
            return new QueryBuilder ( ) ;
        }

        protected virtual ObjectArchieveStorageBuilder CreateStorageBuilder ( ) 
        {
            return new ObjectArchieveStorageBuilder ( ) ;
        }

        protected virtual IList<string> GetValues ( IDicomDataParameter condition )
        {
            if ( condition is RangeMatching )
            {
                RangeMatching  rangeCondition  = (RangeMatching) condition ;
                fo.DicomItem dateElement     = rangeCondition.DateElement ;
                fo.DicomItem timeElement     = rangeCondition.TimeElement ;
                
                
                return GetDateTimeValues ( (fo.DicomElement) dateElement, (fo.DicomElement) timeElement ) ;
            }
            else if ( condition.VR.Equals ( fo.DicomVR.DA ) || condition.VR.Equals ( fo.DicomVR.DT ) )
            {
                fo.DicomElement dateElement = null ;
                fo.DicomElement timeElement = null ;

                foreach ( var element in condition.Elements )
                {
                    if ( element.ValueRepresentation.Equals ( fo.DicomVR.DA ) )
                    {
                        dateElement = (fo.DicomElement) element ;
                        continue ;
                    }

                    if ( element.ValueRepresentation.Equals ( fo.DicomVR.TM ) )
                    { 
                        timeElement = (fo.DicomElement) element ;
                    }
                }

                return GetDateTimeValues ( dateElement, timeElement ) ;
            }
            else
            { 
                return condition.GetValues ( ) ;
            }
        }

        private IList<string> GetDateTimeValues ( fo.DicomElement dateElement, fo.DicomElement timeElement )
        {
            List<string> values = new List<string> ( ) ; 
            int dateValuesCount = dateElement == null ? 0 : (int)dateElement.Count;
            int timeValuesCount = timeElement == null ? 0 : (int)timeElement.Count;
            int dateTimeIndex = 0;

            for (; dateTimeIndex < dateValuesCount || dateTimeIndex < timeValuesCount; dateTimeIndex++)
            {
                string dateString = null;
                string timeString = null;

                if (dateTimeIndex < dateValuesCount)
                {
                    dateString = dateElement == null || dateElement.Count == 0 ? null : dateElement.Get<string>(0); //TODO: test - original code returns "" as default
                }

                if (dateTimeIndex < dateValuesCount)
                {
                    timeString = timeElement == null || timeElement.Count == 0 ? null : timeElement.Get<string>(0); //TODO: test - original code returns "" as default
                }

                values.AddRange(GetDateTimeValues(dateString, timeString));
            }

            return values;
        }

        protected virtual IList<string> GetDateTimeValues ( string dateString, string timeString )
        {
            string date1String = null ;
            string time1String = null ;
            string date2String = null ;
            string time2String = null ;

            if ( dateString != null )
            {
                dateString = dateString.Trim();

                if (!string.IsNullOrWhiteSpace(dateString) )
                {
                    string[] dateRange = dateString.Split('-');

                    if (dateRange.Length > 0)
                    {
                        date1String = dateRange[0];
                        time1String = "";
                    }

                    if (dateRange.Length == 2)
                    {
                        date2String = dateRange[1];
                        time2String = "";
                    }
                }
            }


            if ( timeString != null )
            { 
                timeString = timeString.Trim ( ) ;

                if ( !string.IsNullOrWhiteSpace ( timeString ) )
                { 
                    string[] timeRange = timeString.Split ( '-' ) ;

                    if ( timeRange.Length > 0 )
                    { 
                        date1String = date1String ?? "" ;
                        time1String = timeRange [0 ] ; 
                    }

                    if ( timeRange.Length == 2 )
                    { 
                        date2String = date2String ?? "" ;
                        time2String = timeRange [ 1 ] ;
                    }
                }
            }
        
            return GetDateTimeQueryValues ( date1String, time1String, dateString, time2String ) ;
        }

        protected virtual IList<string> GetDateTimeQueryValues
        (
            string date1String, 
            string time1String, 
            string date2String, 
            string time2String
        )
        {
            List<string> values = new List<string> ( ) ;
            
            
            SanitizeDate ( ref date1String ) ;
            SanitizeDate ( ref date2String ) ;
            SanitizeTime ( ref time1String, true ) ;
            SanitizeTime ( ref time2String, false ) ;

            if ( string.IsNullOrEmpty (date1String) && string.IsNullOrEmpty(date2String) &&
                 string.IsNullOrEmpty (time1String) && string.IsNullOrEmpty(time2String) )
            {
                return values ;
            }

            if ( string.IsNullOrEmpty(date1String) ) 
            {
                //date should be either min or same as second
                date1String = string.IsNullOrEmpty ( date2String ) ? SqlConstants.MinDate : date2String  ;
            }

            if ( string.IsNullOrEmpty (time1String) )
            {
                time1String = string.IsNullOrEmpty ( time2String ) ? SqlConstants.MinTime : time2String ;
            }

            if ( string.IsNullOrEmpty(date2String) ) 
            {
                //date should be either min or same as second
                date2String = ( SqlConstants.MinDate == date1String ) ? SqlConstants.MaxDate : date1String ;
            }

            if ( string.IsNullOrEmpty (time2String) )
            {
                time2String = ( SqlConstants.MinTime == time1String ) ? SqlConstants.MaxTime : time1String ;
            } 

            values.Add ( date1String + " " + time1String ) ;
            values.Add ( date2String + " " + time2String ) ;
            
            return values ;
        }

        protected virtual InstanceMetadata CreateMetadata ( string columnName, object metaValue )
        {
            if( null != metaValue )
            {
                return metaValue.ToString ( ).FromJson<InstanceMetadata> ( ) ;
            }

            return null ; 
        }
                
        //TODO: currently not used any more
        protected virtual string CombineDateTime(string dateString, string timeString, bool secondInRange )
        {
            if ( string.IsNullOrWhiteSpace ( timeString ) && string.IsNullOrWhiteSpace ( dateString ) )
            {
                return ( secondInRange ) ? SqlConstants.MaxDateTime : SqlConstants.MinDateTime ;
            }

            if ( string.IsNullOrEmpty ( timeString ) )
            {
                timeString = ( secondInRange ) ? SqlConstants.MaxTime : SqlConstants.MinTime ;
            }

            if ( string.IsNullOrEmpty ( dateString ) )
            {
                dateString = ( secondInRange ) ? SqlConstants.MaxDate : SqlConstants.MinDate ;
            }
            

            return dateString + " " + timeString ;
        }

        protected virtual void SanitizeTime(ref string timeString, bool startTime )
        {
            if (null == timeString) { return ;}

            if ( string.IsNullOrEmpty ( timeString ) )
            { 
                timeString = "" ;

                return ;
            }

            if ( true )//TODO: add to config
            {
                timeString = timeString.Replace (":", "");
            }

            int length = timeString.Length ;

            if (length > "hhmm".Length) 
            {  
                timeString = timeString.Insert (4, ":") ; 
            }
            else if ( length == 4 )
            { 
                if ( startTime )
                {
                    timeString   += ":00" ;
                }
                else
                { 
                    timeString += ":59" ;
                }
            }
            
            if (timeString.Length > "hh".Length) 
            {  
                timeString = timeString.Insert (2, ":") ; 
            }
            else //it must equal
            { 
                if ( startTime )
                {
                    timeString   += ":00:00" ;
                }
                else
                { 
                    timeString += ":59:59" ;
                }
            }
            
            {//TODO: no support for fractions 
                int fractionsIndex ;

                if( ( fractionsIndex= timeString.LastIndexOf (".") ) > -1 )
                {
                    timeString = timeString.Substring ( 0, fractionsIndex ) ;
                }
            } 
        }

        protected virtual void SanitizeDate(ref string dateString )
        {
            if (null == dateString) { return ;}

            //TODO: make it a configuration option
            //a lot of dataset samples do not follow dicom standard
            //must be caled prior to IsMinDate
            if (true)
            {   
                dateString = dateString.Replace ( ".", "" ).Replace ( "-", "") ;
            }
            
            if ( string.IsNullOrEmpty ( dateString) || IsMinDate ( dateString ) )
            { 
                dateString = "" ;

                return ;
            }

            int length = dateString.Length ;

            if (length != 8) {  throw new ArgumentException ( "Invalid date value") ; }
            
            dateString = dateString.Insert ( 6, "-") ;

            dateString = dateString.Insert ( 4, "-") ;
        }

        private static bool IsMinDate(string dateString)
        {
            return ( DateTime.MinValue.ToShortDateString() == 
                     DateTime.ParseExact(dateString, "yyyymmdd", System.Globalization.CultureInfo.InvariantCulture).ToShortDateString());
        }
        
        public static class SqlConstants
        {
            public static string MinDate = "1753/1/1" ;
            public static string MaxDate = "9999/12/31" ;
            public static string MinTime = "00:00:00" ;
            public static string MaxTime = "23:59:59" ;

            public static string MaxDateTime = "9999/12/31 11:59:59"   ;
            public static string MinDateTime = "1753/1/1 00:00:00" ;
        }
    }
}
