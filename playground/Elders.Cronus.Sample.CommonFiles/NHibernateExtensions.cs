﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity.Design.PluralizationServices;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Elders.Cronus.DomainModeling;
using NHibernate;
using NHibernate.Cfg;
using NHibernate.Collection;
using NHibernate.DebugHelpers;
using NHibernate.Engine;
using NHibernate.Loader;
using NHibernate.Mapping.ByCode;
using NHibernate.Persister.Collection;
using NHibernate.SqlTypes;
using NHibernate.Tool.hbm2ddl;
using NHibernate.Type;
using NHibernate.UserTypes;
using NHibernate.Util;

namespace Elders.Cronus.Sample.CommonFiles
{
    public static class NHibernateExtensions
    {
        /// <summary>
        /// Adds all Hyperion mappings to a NHibernate configuration.
        /// </summary>
        /// <param name="nhConf">The NHib configuration instance.</param>
        /// <returns>Returns the NHib configuration instance with Hyperion mappings.</returns>
        public static Configuration AddAutoMappings(this Configuration nhConf, IEnumerable<Type> types, Action<ModelMapper> modelMapper = null)
        {
            var englishPluralizationService = PluralizationService.CreateService(new CultureInfo("en-US"));
            var mapper = new ModelMapper(new HyperionModelInspector());

            //var baseEntityType = typeof(IEventHandler);
            //mapper.IsEntity((t, declared) => baseEntityType.IsAssignableFrom(t) && baseEntityType != t && !t.IsInterface);
            //mapper.IsRootEntity((t, declared) => baseEntityType.Equals(t.BaseType));

            mapper.BeforeMapClass += (insp, prop, map) => map.Table(englishPluralizationService.Pluralize(prop.Name));

            mapper.BeforeMapClass += (i, t, cm) =>
            {

                var memberInfo = t.GetProperty("Id", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var propType = memberInfo.PropertyType;
                if (typeof(AggregateRootId).IsAssignableFrom(propType))
                {

                    cm.ComponentAsId(memberInfo, x =>
                    {
                        //   var props = memberInfo.PropertyType.GetProperty("Id", BindingFlags.Instance | BindingFlags.NonPublic);
                        var property = memberInfo.PropertyType.GetProperty("Id");
                        x.Property(property, y => y.Column(t.Name + "Id"));
                    });
                }
                else
                {
                    cm.Id(map =>
                    {
                        map.Column(t.Name + "Id");

                        if (propType == typeof(Int32))
                            map.Generator(Generators.Identity);
                        else
                            map.Generator(Generators.Assigned);
                    });
                }
            };
            mapper.BeforeMapProperty += (modelInsperctor, member, propertyCustomizer) =>
            {
                if (typeof(AggregateRootId).IsAssignableFrom(member.LocalMember.GetPropertyOrFieldType()))
                {
                    var generic = typeof(AggregateIdUserType<>);
                    var t = member.LocalMember.GetPropertyOrFieldType();
                    var actualType = generic.MakeGenericType(t);
                    propertyCustomizer.Type(actualType, null);


                }
                else if (typeof(DateTime) == member.LocalMember.GetPropertyOrFieldType())
                {

                    propertyCustomizer.Type(typeof(FileTimeUtc), null);

                }
            };
            mapper.BeforeMapManyToOne += (insp, prop, map) => map.Column(prop.LocalMember.GetPropertyOrFieldType().Name + "Id");
            mapper.BeforeMapManyToOne += (insp, prop, map) => map.Cascade(NHibernate.Mapping.ByCode.Cascade.None);

            mapper.BeforeMapBag += (insp, prop, map) => map.Key(km => km.Column(prop.GetContainerEntity(insp).Name + "Id"));
            mapper.BeforeMapBag += (insp, prop, map) =>
            {
                map.Cascade(NHibernate.Mapping.ByCode.Cascade.All.Include(NHibernate.Mapping.ByCode.Cascade.DeleteOrphans));
                map.Inverse(true);
            };


            mapper.BeforeMapSet += (insp, prop, map) => map.Key(km => km.Column(prop.GetContainerEntity(insp).Name + "Id"));
            mapper.BeforeMapSet += (insp, prop, map) =>
            {
                map.Cascade(NHibernate.Mapping.ByCode.Cascade.All.Include(NHibernate.Mapping.ByCode.Cascade.DeleteOrphans));
                map.Inverse(true);
            };

            if (modelMapper != null)
                modelMapper(mapper);

            var mapping = mapper.CompileMappingFor(types);

            nhConf.AddDeserializedMapping(mapping, "Hyperion");

            return nhConf;
        }



        /// <summary>
        /// Drops the database based on the mappings.
        /// </summary>
        /// <param name="nhConf">The NHib configuration instance.</param>
        /// <returns>Returns the NHib configuration instance.</returns>
        public static Configuration DropTables(this Configuration nhConf)
        {
            new SchemaExport(nhConf).Drop(false, true);
            return nhConf;
        }

        /// <summary>
        /// Drops and creates the database based on the mappings.
        /// </summary>
        /// <param name="nhConf">The NHib configuration instance.</param>
        /// <returns>Returns the NHib configuration instance.</returns>
        public static Configuration CreateTables(this Configuration nhConf)
        {
            new SchemaExport(nhConf).Create(false, true);
            return nhConf;
        }

        public static Configuration CreateDatabase(this Configuration nhConf, string connectionString = null)
        {
            string conString =
                connectionString ??
                nhConf.GetProperty(NHibernate.Cfg.Environment.ConnectionString) ??
                System.Configuration.ConfigurationManager.ConnectionStrings[nhConf.GetProperty(NHibernate.Cfg.Environment.ConnectionStringName)].ConnectionString;

            if (!DatabaseManager.DatabaseExists(conString))
            {
                DatabaseManager.CreateDatabase(conString, enableSnapshotIsolation: true);
                nhConf.CreateTables();
            }

            return nhConf;
        }

        public static Configuration CreateDatabase_AND_OVERWRITE_EXISTING_DATABASE(this Configuration nhConf, string connectionString = null)
        {
            string conString =
                connectionString ??
                nhConf.GetProperty(NHibernate.Cfg.Environment.ConnectionString) ??
                System.Configuration.ConfigurationManager.ConnectionStrings[nhConf.GetProperty(NHibernate.Cfg.Environment.ConnectionStringName)].ConnectionString;

            if (DatabaseManager.DatabaseExists(conString))
                DatabaseManager.DeleteDatabase(conString);

            DatabaseManager.CreateDatabase(conString, enableSnapshotIsolation: true);
            nhConf.CreateTables();

            return nhConf;
        }
    }

    [Serializable]
    public sealed class BinaryWithProtobufUserType : NHibernate.UserTypes.IUserType
    {
        private Byte[] data = null;

        public static Protoreg.ProtoregSerializer Serializer { get; set; }

        public Boolean IsMutable { get { return false; } }

        public Object Assemble(Object cached, Object owner)
        {
            return (cached);
        }

        public Object DeepCopy(Object value)
        {
            return (value);
        }

        public Object Disassemble(Object value)
        {
            return (value);
        }

        public new Boolean Equals(Object x, Object y)
        {
            return (Object.Equals(x, y));
        }

        public Int32 GetHashCode(Object x)
        {
            return ((x != null) ? x.GetHashCode() : 0);
        }

        public override Int32 GetHashCode()
        {
            return ((this.data != null) ? this.data.GetHashCode() : 0);
        }

        public override Boolean Equals(Object obj)
        {
            BinaryWithProtobufUserType other = obj as BinaryWithProtobufUserType;

            if (other == null)
            {
                return (false);
            }

            if (Object.ReferenceEquals(this, other) == true)
            {
                return (true);
            }

            return (this.data.SequenceEqual(other.data));
        }

        public Object NullSafeGet(System.Data.IDataReader rs, String[] names, Object owner)
        {
            Int32 index = rs.GetOrdinal(names[0]);
            Byte[] data = rs.GetValue(index) as Byte[];

            this.data = data as Byte[];

            if (data == null)
            {
                return (null);
            }

            using (MemoryStream stream = new MemoryStream(this.data ?? new Byte[0]))
            {
                var desObject = Serializer.Deserialize(stream);

                return desObject;
            }
        }

        public void NullSafeSet(System.Data.IDbCommand cmd, Object value, Int32 index)
        {
            if (value != null)
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    Serializer.Serialize(stream, value);
                    value = stream.ToArray();
                }
            }

            (cmd.Parameters[index] as System.Data.Common.DbParameter).Value = value ?? DBNull.Value;
        }

        public Object Replace(Object original, Object target, Object owner)
        {
            return (original);
        }

        public Type ReturnedType
        {
            get
            {
                return (typeof(List<Elders.Cronus.DomainModeling.IEvent>));
            }
        }

        public NHibernate.SqlTypes.SqlType[] SqlTypes
        {
            get
            {
                return (new NHibernate.SqlTypes.SqlType[] { new NHibernate.SqlTypes.SqlType(System.Data.DbType.Binary) });
            }
        }
    }

    public class AggregateIdUserType<T> : IUserType where T : GuidId
    {
        bool IUserType.Equals(object x, object y)
        {
            if (ReferenceEquals(null, x) && ReferenceEquals(null, y))
                return true;
            if (ReferenceEquals(null, x))
                return false;
            else
                return x.Equals(y);
        }

        public object Assemble(object cached, object owner)
        {
            return cached;
        }

        public object DeepCopy(object value)
        {
            var copy = Activator.CreateInstance(typeof(T), ((T)value).Id);
            return copy;
        }

        public object Disassemble(object value)
        {
            return value;
        }

        public int GetHashCode(object x)
        {
            return x.GetHashCode();
        }

        public bool IsMutable
        {
            get { return true; }
        }

        public object NullSafeGet(System.Data.IDataReader rs, string[] names, object owner)
        {
            var index = rs.GetOrdinal(names[0]);
            if (rs.IsDBNull(index) || ((Guid)rs[index]) == default(Guid))
                return null;

            var result = Activator.CreateInstance(typeof(T), (Guid)rs[index]);
            return ((T)result);
            ;
        }

        public void NullSafeSet(System.Data.IDbCommand cmd, object value, int index)
        {
            if (value == null || value == DBNull.Value)
                NHibernateUtil.Guid.NullSafeSet(cmd, null, index);

            NHibernateUtil.Guid.Set(cmd, ((T)value).Id, index);
        }

        public object Replace(object original, object target, object owner)
        {
            return original;
        }

        public Type ReturnedType
        {
            get { return typeof(T); }
        }

        public NHibernate.SqlTypes.SqlType[] SqlTypes
        {
            get { return new SqlType[] { NHibernateUtil.Guid.SqlType }; }
        }
    }

    public class FileTimeUtc : NHibernate.UserTypes.IUserType
    {
        public SqlType[] SqlTypes
        {
            get
            {
                SqlType[] types = new SqlType[1];
                types[0] = new SqlType(DbType.Int64);
                return types;
            }
        }

        public System.Type ReturnedType
        {
            get { return typeof(DateTime); }
        }

        public new bool Equals(object x, object y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x == null)
            {
                return false;
            }
            else
            {
                return x.Equals(y);
            }
        }

        public int GetHashCode(object x)
        {
            return x.GetHashCode();
        }

        public object NullSafeGet(IDataReader rs, string[] names, object owner)
        {
            object results = NHibernateUtil.Int64.NullSafeGet(rs, names[0]);

            if (results == null)
                return null;

            long utcValue = (long)results;

            DateTime result = DateTime.FromFileTimeUtc(utcValue);
            return result;
        }

        public void NullSafeSet(IDbCommand cmd, object value, int index)
        {

            DateTime val = (DateTime)value;
            if (value == null || val == null)
            {
                NHibernateUtil.Int64.NullSafeSet(cmd, null, index);
                return;
            }
            long utcDatetime = val.ToFileTimeUtc();
            NHibernateUtil.Int64.NullSafeSet(cmd, utcDatetime, index);
        }

        public object DeepCopy(object value)
        {
            //We deep copy the uri by creating a new instance with the same contents
            if (value == null) return null;

            DateTime deepCopy = (DateTime)value;
            return deepCopy;
        }

        public bool IsMutable
        {
            get { return false; }
        }

        public object Replace(object original, object target, object owner)
        {
            //As our object is immutable we can just return the original
            return original;
        }

        public object Assemble(object cached, object owner)
        {
            //Used for casching, as our object is immutable we can just return it as is
            return DeepCopy(cached);
        }

        public object Disassemble(object value)
        {
            //Used for casching, as our object is immutable we can just return it as is
            return DeepCopy(value);
        }
    }

    public class GuidListUserType : IUserType
    {
        private const char cStringSeparator = '#';

        bool IUserType.Equals(object x, object y)
        {
            if (x == null || y == null) return false;
            List<Guid> xl = (List<Guid>)x;
            List<Guid> yl = (List<Guid>)y;
            if (xl.Count != yl.Count) return false;
            Boolean retvalue = xl.Except(yl).Count() == 0;
            return retvalue;
        }

        public object Assemble(object cached, object owner)
        {
            return cached;
        }

        public object DeepCopy(object value)
        {
            List<Guid> obj = (List<Guid>)value;
            List<Guid> retvalue = new List<Guid>(obj);

            return retvalue;
        }

        public object Disassemble(object value)
        {
            return value;
        }

        public int GetHashCode(object x)
        {
            return x.GetHashCode();
        }

        public bool IsMutable
        {
            get { return true; }
        }

        public object NullSafeGet(System.Data.IDataReader rs, string[] names, object owner)
        {
            List<Guid> result = new List<Guid>();
            Int32 index = rs.GetOrdinal(names[0]);
            if (rs.IsDBNull(index) || String.IsNullOrEmpty((String)rs[index]))
                return result;
            foreach (String s in ((String)rs[index]).Split(cStringSeparator))
            {
                var id = Guid.Parse(s);
                result.Add(id);
            }
            return result;
        }

        public void NullSafeSet(System.Data.IDbCommand cmd, object value, int index)
        {
            if (value == null || value == DBNull.Value)
            {
                NHibernateUtil.StringClob.NullSafeSet(cmd, null, index);
            }
            List<Guid> stringList = (List<Guid>)value;
            StringBuilder sb = new StringBuilder();
            foreach (Guid s in stringList)
            {
                sb.Append(s);
                sb.Append(cStringSeparator);
            }
            if (sb.Length > 0) sb.Length--;
            NHibernateUtil.StringClob.Set(cmd, sb.ToString(), index);
        }

        public object Replace(object original, object target, object owner)
        {
            return original;
        }

        public Type ReturnedType
        {
            get { return typeof(IList<String>); }
        }

        public NHibernate.SqlTypes.SqlType[] SqlTypes
        {
            get { return new SqlType[] { NHibernateUtil.StringClob.SqlType }; }
        }
    }

    public class GuidSetUserType : IUserType
    {
        private const char cStringSeparator = '#';

        bool IUserType.Equals(object x, object y)
        {
            if (x == null || y == null) return false;
            HashSet<Guid> xl = (HashSet<Guid>)x;
            HashSet<Guid> yl = (HashSet<Guid>)y;
            if (xl.Count != yl.Count) return false;
            Boolean retvalue = xl.Except(yl).Count() == 0;
            return retvalue;
        }

        public object Assemble(object cached, object owner)
        {
            return cached;
        }

        public object DeepCopy(object value)
        {
            HashSet<Guid> obj = (HashSet<Guid>)value;
            HashSet<Guid> retvalue = new HashSet<Guid>(obj);

            return retvalue;
        }

        public object Disassemble(object value)
        {
            return value;
        }

        public int GetHashCode(object x)
        {
            return x.GetHashCode();
        }

        public bool IsMutable
        {
            get { return true; }
        }

        public object NullSafeGet(System.Data.IDataReader rs, string[] names, object owner)
        {
            HashSet<Guid> result = new HashSet<Guid>();
            Int32 index = rs.GetOrdinal(names[0]);
            if (rs.IsDBNull(index) || String.IsNullOrEmpty((String)rs[index]))
                return result;
            foreach (String s in ((String)rs[index]).Split(cStringSeparator))
            {
                var id = Guid.Parse(s);
                result.Add(id);
            }
            return result;
        }

        public void NullSafeSet(System.Data.IDbCommand cmd, object value, int index)
        {
            if (value == null || value == DBNull.Value)
            {
                NHibernateUtil.StringClob.NullSafeSet(cmd, null, index);
            }
            HashSet<Guid> stringList = (HashSet<Guid>)value;
            StringBuilder sb = new StringBuilder();
            foreach (Guid s in stringList)
            {
                sb.Append(s);
                sb.Append(cStringSeparator);
            }
            if (sb.Length > 0) sb.Length--;
            NHibernateUtil.StringClob.Set(cmd, sb.ToString(), index);
        }

        public object Replace(object original, object target, object owner)
        {
            return original;
        }

        public Type ReturnedType
        {
            get { return typeof(ISet<String>); }
        }

        public NHibernate.SqlTypes.SqlType[] SqlTypes
        {
            get { return new SqlType[] { NHibernateUtil.StringClob.SqlType }; }
        }
    }

    public class Net4CollectionTypeFactory : DefaultCollectionTypeFactory
    {
        public override CollectionType Set<T>(string role, string propertyRef, bool embedded)
        {
            return new GenericSetType<T>(role, propertyRef);
        }

        public override CollectionType SortedSet<T>(string role, string propertyRef, bool embedded, IComparer<T> comparer)
        {
            return new GenericSortedSetType<T>(role, propertyRef, comparer);
        }
    }

    [Serializable]
    public class GenericSortedSetType<T> : GenericSetType<T>
    {
        private readonly IComparer<T> comparer;

        public GenericSortedSetType(string role, string propertyRef, IComparer<T> comparer)
            : base(role, propertyRef)
        {
            this.comparer = comparer;
        }

        public override object Instantiate(int anticipatedSize)
        {
            return new SortedSet<T>(this.comparer);
        }

        public IComparer<T> Comparer
        {
            get
            {
                return this.comparer;
            }
        }
    }

    /// <summary>
    /// A <see cref="IModelInspector"/> which allows customization of conditions with explicitly declared members.
    /// </summary>
    public class HyperionModelInspector : IModelInspector, IModelExplicitDeclarationsHolder
    {
        private class MixinDeclaredModel : AbstractExplicitlyDeclaredModel
        {
            private readonly IModelInspector inspector;

            public MixinDeclaredModel(IModelInspector inspector)
            {
                this.inspector = inspector;
            }

            public override bool IsComponent(System.Type type)
            {
                return Components.Contains(type);
            }

            public override bool IsRootEntity(System.Type entityType)
            {
                return inspector.IsRootEntity(entityType);
            }

            public bool IsEntity(System.Type type)
            {
                return RootEntities.Contains(type) || type.GetBaseTypes().Any(t => RootEntities.Contains(t)) || HasDelayedEntityRegistration(type);
            }

            public bool IsTablePerClass(System.Type type)
            {
                ExecuteDelayedTypeRegistration(type);
                return IsMappedForTablePerClassEntities(type);
            }

            public bool IsTablePerClassSplit(System.Type type, object splitGroupId, MemberInfo member)
            {
                return Equals(splitGroupId, GetSplitGroupFor(member));
            }

            public bool IsTablePerClassHierarchy(System.Type type)
            {
                ExecuteDelayedTypeRegistration(type);
                return IsMappedForTablePerClassHierarchyEntities(type);
            }

            public bool IsTablePerConcreteClass(System.Type type)
            {
                ExecuteDelayedTypeRegistration(type);
                return IsMappedForTablePerConcreteClassEntities(type);
            }

            public bool IsOneToOne(MemberInfo member)
            {
                return OneToOneRelations.Contains(member);
            }

            public bool IsManyToOne(MemberInfo member)
            {
                if (typeof(AggregateRootId).IsAssignableFrom(member.GetPropertyOrFieldType()))
                    return false;
                else
                    return ManyToOneRelations.Contains(member);
            }

            public bool IsManyToMany(MemberInfo member)
            {
                return ManyToManyRelations.Contains(member);
            }

            public bool IsOneToMany(MemberInfo member)
            {
                return OneToManyRelations.Contains(member);
            }

            public bool IsManyToAny(MemberInfo member)
            {
                return ManyToAnyRelations.Contains(member);
            }

            public bool IsAny(MemberInfo member)
            {
                return Any.Contains(member);
            }

            public bool IsPersistentId(MemberInfo member)
            {
                return Poids.Contains(member);
            }

            public bool IsMemberOfComposedId(MemberInfo member)
            {
                return ComposedIds.Contains(member);
            }

            public bool IsVersion(MemberInfo member)
            {
                return VersionProperties.Contains(member);
            }

            public bool IsMemberOfNaturalId(MemberInfo member)
            {
                return NaturalIds.Contains(member);
            }

            public bool IsPersistentProperty(MemberInfo member)
            {
                if (typeof(AggregateRootId).IsAssignableFrom(member.GetPropertyOrFieldType()))
                    return true;
                else
                    return PersistentMembers.Contains(member);
            }

            public bool IsSet(MemberInfo role)
            {
                return Sets.Contains(role);
            }

            public bool IsBag(MemberInfo role)
            {
                return Bags.Contains(role);
            }

            public bool IsIdBag(MemberInfo role)
            {
                return IdBags.Contains(role);
            }

            public bool IsList(MemberInfo role)
            {
                return Lists.Contains(role);
            }

            public bool IsArray(MemberInfo role)
            {
                return Arrays.Contains(role);
            }

            public bool IsDictionary(MemberInfo role)
            {
                return Dictionaries.Contains(role);
            }

            public bool IsProperty(MemberInfo member)
            {
                if (typeof(AggregateRootId).IsAssignableFrom(member.GetPropertyOrFieldType()))
                    return true;
                return Properties.Contains(member);
            }

            public bool IsDynamicComponent(MemberInfo member)
            {
                return DynamicComponents.Contains(member);
            }

            public IEnumerable<string> GetPropertiesSplits(System.Type type)
            {
                return GetSplitGroupsFor(type);
            }
        }

        private readonly MixinDeclaredModel declaredModel;

        private Func<System.Type, bool, bool> isEntity = (t, declared) => declared;
        private Func<System.Type, bool, bool> isRootEntity;
        private Func<System.Type, bool, bool> isTablePerClass;
        private Func<SplitDefinition, bool, bool> isTablePerClassSplit = (sd, declared) => declared;
        private Func<System.Type, bool, bool> isTablePerClassHierarchy = (t, declared) => declared;
        private Func<System.Type, bool, bool> isTablePerConcreteClass = (t, declared) => declared;
        private Func<System.Type, IEnumerable<string>, IEnumerable<string>> splitsForType = (t, declared) => declared;
        private Func<System.Type, bool, bool> isComponent;

        private Func<MemberInfo, bool, bool> isPersistentId;
        private Func<MemberInfo, bool, bool> isPersistentProperty;
        private Func<MemberInfo, bool, bool> isVersion = (m, declared) => declared;

        private Func<MemberInfo, bool, bool> isProperty = (m, declared) => declared;
        private Func<MemberInfo, bool, bool> isDynamicComponent = (m, declared) => declared;
        private Func<MemberInfo, bool, bool> isAny = (m, declared) => declared;
        private Func<MemberInfo, bool, bool> isManyToMany = (m, declared) => declared;
        private Func<MemberInfo, bool, bool> isManyToAny = (m, declared) => declared;
        private Func<MemberInfo, bool, bool> isManyToOne;
        private Func<MemberInfo, bool, bool> isMemberOfNaturalId = (m, declared) => declared;
        private Func<MemberInfo, bool, bool> isOneToMany;
        private Func<MemberInfo, bool, bool> isOneToOne = (m, declared) => declared;

        private Func<MemberInfo, bool, bool> isSet;
        private Func<MemberInfo, bool, bool> isArray;
        private Func<MemberInfo, bool, bool> isBag;
        private Func<MemberInfo, bool, bool> isDictionary;
        private Func<MemberInfo, bool, bool> isIdBag = (m, declared) => declared;
        private Func<MemberInfo, bool, bool> isList = (m, declared) => declared;

        public HyperionModelInspector()
        {
            isEntity = (t, declared) => declared || MatchEntity(t);
            isRootEntity = (t, declared) => declared || MatchRootEntity(t);
            isTablePerClass = (t, declared) => declared || MatchTablePerClass(t);
            isPersistentId = (m, declared) => declared || MatchPoIdPattern(m);
            isComponent = (t, declared) => declared || MatchComponentPattern(t);
            isPersistentProperty = (m, declared) => declared || ((m is PropertyInfo) && MatchNoReadOnlyPropertyPattern(m));
            isSet = (m, declared) => declared || MatchCollection(m, MatchSetMember);
            isArray = (m, declared) => declared || MatchCollection(m, MatchArrayMember);
            isBag = (m, declared) => declared || MatchCollection(m, MatchBagMember);
            isDictionary = (m, declared) => declared || MatchCollection(m, MatchDictionaryMember);
            isManyToOne = (m, declared) => declared || MatchManyToOne(m);
            isOneToMany = (m, declared) => declared || MatchOneToMany(m);
            declaredModel = new MixinDeclaredModel(this);
        }

        private bool MatchRootEntity(System.Type type)
        {
            return type.IsClass && typeof(object).Equals(type.BaseType) && ((IModelInspector)this).IsEntity(type);
        }

        private bool MatchTablePerClass(System.Type type)
        {
            return !declaredModel.IsTablePerClassHierarchy(type) && !declaredModel.IsTablePerConcreteClass(type);
        }

        private bool MatchOneToMany(MemberInfo memberInfo)
        {
            var modelInspector = (IModelInspector)this;
            System.Type from = memberInfo.ReflectedType;
            System.Type to = memberInfo.GetPropertyOrFieldType().DetermineCollectionElementOrDictionaryValueType();
            if (to == null)
            {
                // no generic collection or simple property
                return false;
            }
            bool areEntities = modelInspector.IsEntity(from) && modelInspector.IsEntity(to);
            bool isFromComponentToEntity = modelInspector.IsComponent(from) && modelInspector.IsEntity(to);
            return !declaredModel.IsManyToMany(memberInfo) && (areEntities || isFromComponentToEntity);
        }

        private bool MatchManyToOne(MemberInfo memberInfo)
        {
            var modelInspector = (IModelInspector)this;
            System.Type from = memberInfo.ReflectedType;
            System.Type to = memberInfo.GetPropertyOrFieldType();

            bool areEntities = modelInspector.IsEntity(from) && modelInspector.IsEntity(to);
            bool isFromComponentToEntity = modelInspector.IsComponent(from) && modelInspector.IsEntity(to);
            return isFromComponentToEntity || (areEntities && !modelInspector.IsOneToOne(memberInfo));
        }

        protected bool MatchArrayMember(MemberInfo subject)
        {
            System.Type memberType = subject.GetPropertyOrFieldType();
            return memberType.IsArray && memberType.GetElementType() != typeof(byte);
        }

        protected bool MatchDictionaryMember(MemberInfo subject)
        {
            System.Type memberType = subject.GetPropertyOrFieldType();
            if (typeof(System.Collections.IDictionary).IsAssignableFrom(memberType))
            {
                return true;
            }
            if (memberType.IsGenericType)
            {
                return memberType.GetGenericInterfaceTypeDefinitions().Contains(typeof(IDictionary<,>));
            }
            return false;
        }

        protected bool MatchBagMember(MemberInfo subject)
        {
            System.Type memberType = subject.GetPropertyOrFieldType();
            return typeof(System.Collections.IEnumerable).IsAssignableFrom(memberType) && !(memberType == typeof(string) || memberType == typeof(byte[]));
        }

        protected bool MatchCollection(MemberInfo subject, Predicate<MemberInfo> specificCollectionPredicate)
        {
            const BindingFlags defaultBinding = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            if (specificCollectionPredicate(subject)) return true;
            var pi = subject as PropertyInfo;
            if (pi != null)
            {
                var fieldInfo = (from ps in PropertyToField.DefaultStrategies.Values
                                 let fi = subject.DeclaringType.GetField(ps.GetFieldName(pi.Name), defaultBinding)
                                 where fi != null
                                 select fi).FirstOrDefault();

                if (fieldInfo != null)
                {
                    return specificCollectionPredicate(fieldInfo);
                }
            }
            return false;
        }

        protected bool MatchSetMember(MemberInfo subject)
        {
            var memberType = subject.GetPropertyOrFieldType();

            if (memberType.IsGenericType)
            {
                return memberType.GetGenericInterfaceTypeDefinitions().Contains(typeof(ISet<>));
            }
            return false;
        }

        protected bool MatchNoReadOnlyPropertyPattern(MemberInfo subject)
        {
            var isReadOnlyProperty = IsReadOnlyProperty(subject);
            return !isReadOnlyProperty;
        }

        protected bool IsReadOnlyProperty(MemberInfo subject)
        {
            const BindingFlags defaultBinding = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            var property = subject as PropertyInfo;
            if (property == null)
            {
                return false;
            }
            if (CanReadCantWriteInsideType(property) || CanReadCantWriteInBaseType(property))
            {
                return !PropertyToField.DefaultStrategies.Values.Any(s => subject.DeclaringType.GetField(s.GetFieldName(property.Name), defaultBinding) != null) || IsAutoproperty(property);
            }
            return false;
        }

        protected bool IsAutoproperty(PropertyInfo property)
        {
            return property.ReflectedType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                                                                     | BindingFlags.DeclaredOnly).Any(pi => pi.Name == string.Concat("<", property.Name, ">k__BackingField"));
        }

        protected bool CanReadCantWriteInsideType(PropertyInfo property)
        {
            return !property.CanWrite && property.CanRead && property.DeclaringType == property.ReflectedType;
        }

        protected bool CanReadCantWriteInBaseType(PropertyInfo property)
        {
            if (property.DeclaringType == property.ReflectedType)
            {
                return false;
            }
            var rfprop = property.DeclaringType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                                                                     | BindingFlags.DeclaredOnly).SingleOrDefault(pi => pi.Name == property.Name);
            return rfprop != null && !rfprop.CanWrite && rfprop.CanRead;
        }

        protected bool MatchPoIdPattern(MemberInfo subject)
        {
            var name = subject.Name;
            return name.Equals("id", StringComparison.InvariantCultureIgnoreCase)
                         || name.Equals("poid", StringComparison.InvariantCultureIgnoreCase)
                         || name.Equals("oid", StringComparison.InvariantCultureIgnoreCase)
                         || (name.StartsWith(subject.DeclaringType.Name) && name.Equals(subject.DeclaringType.Name + "id", StringComparison.InvariantCultureIgnoreCase));
        }

        protected bool MatchComponentPattern(System.Type subject)
        {
            const BindingFlags flattenHierarchyMembers =
                BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            if (declaredModel.IsEntity(subject))
            {
                return false;
            }
            var modelInspector = (IModelInspector)this;
            return !subject.IsEnum && (subject.Namespace == null || !subject.Namespace.StartsWith("System")) /* hack */
                            && !modelInspector.IsEntity(subject)
                            && !subject.GetProperties(flattenHierarchyMembers).Cast<MemberInfo>().Concat(
                        subject.GetFields(flattenHierarchyMembers)).Any(m => modelInspector.IsPersistentId(m));
        }

        protected bool MatchEntity(System.Type subject)
        {
            const BindingFlags flattenHierarchyMembers =
                BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            if (declaredModel.Components.Contains(subject))
            {
                return false;
            }
            var modelInspector = (IModelInspector)this;
            return subject.IsClass &&
                   subject.GetProperties(flattenHierarchyMembers).Cast<MemberInfo>().Concat(subject.GetFields(flattenHierarchyMembers)).Any(m => modelInspector.IsPersistentId(m));
        }

        #region IModelExplicitDeclarationsHolder Members

        IEnumerable<System.Type> IModelExplicitDeclarationsHolder.RootEntities
        {
            get { return declaredModel.RootEntities; }
        }

        IEnumerable<System.Type> IModelExplicitDeclarationsHolder.Components
        {
            get { return declaredModel.Components; }
        }

        IEnumerable<System.Type> IModelExplicitDeclarationsHolder.TablePerClassEntities
        {
            get { return declaredModel.TablePerClassEntities; }
        }

        IEnumerable<System.Type> IModelExplicitDeclarationsHolder.TablePerClassHierarchyEntities
        {
            get { return declaredModel.TablePerClassHierarchyEntities; }
        }

        IEnumerable<System.Type> IModelExplicitDeclarationsHolder.TablePerConcreteClassEntities
        {
            get { return declaredModel.TablePerConcreteClassEntities; }
        }

        IEnumerable<MemberInfo> IModelExplicitDeclarationsHolder.OneToOneRelations
        {
            get { return declaredModel.OneToOneRelations; }
        }

        IEnumerable<MemberInfo> IModelExplicitDeclarationsHolder.ManyToOneRelations
        {
            get { return declaredModel.ManyToManyRelations; }
        }

        IEnumerable<MemberInfo> IModelExplicitDeclarationsHolder.ManyToManyRelations
        {
            get { return declaredModel.ManyToManyRelations; }
        }

        IEnumerable<MemberInfo> IModelExplicitDeclarationsHolder.OneToManyRelations
        {
            get { return declaredModel.OneToManyRelations; }
        }

        IEnumerable<MemberInfo> IModelExplicitDeclarationsHolder.ManyToAnyRelations
        {
            get { return declaredModel.ManyToAnyRelations; }
        }

        IEnumerable<MemberInfo> IModelExplicitDeclarationsHolder.Any
        {
            get { return declaredModel.Any; }
        }

        IEnumerable<MemberInfo> IModelExplicitDeclarationsHolder.Poids
        {
            get { return declaredModel.Poids; }
        }

        IEnumerable<MemberInfo> IModelExplicitDeclarationsHolder.VersionProperties
        {
            get { return declaredModel.VersionProperties; }
        }

        IEnumerable<MemberInfo> IModelExplicitDeclarationsHolder.NaturalIds
        {
            get { return declaredModel.NaturalIds; }
        }

        IEnumerable<MemberInfo> IModelExplicitDeclarationsHolder.Sets
        {
            get { return declaredModel.Sets; }
        }

        IEnumerable<MemberInfo> IModelExplicitDeclarationsHolder.Bags
        {
            get { return declaredModel.Bags; }
        }

        IEnumerable<MemberInfo> IModelExplicitDeclarationsHolder.IdBags
        {
            get { return declaredModel.IdBags; }
        }

        IEnumerable<MemberInfo> IModelExplicitDeclarationsHolder.Lists
        {
            get { return declaredModel.Lists; }
        }

        IEnumerable<MemberInfo> IModelExplicitDeclarationsHolder.Arrays
        {
            get { return declaredModel.Arrays; }
        }

        IEnumerable<MemberInfo> IModelExplicitDeclarationsHolder.Dictionaries
        {
            get { return declaredModel.Dictionaries; }
        }

        IEnumerable<MemberInfo> IModelExplicitDeclarationsHolder.Properties
        {
            get { return declaredModel.Properties; }
        }

        IEnumerable<MemberInfo> IModelExplicitDeclarationsHolder.PersistentMembers
        {
            get { return declaredModel.PersistentMembers; }
        }

        IEnumerable<SplitDefinition> IModelExplicitDeclarationsHolder.SplitDefinitions
        {
            get { return declaredModel.SplitDefinitions; }
        }

        IEnumerable<MemberInfo> IModelExplicitDeclarationsHolder.DynamicComponents
        {
            get { return declaredModel.DynamicComponents; }
        }

        IEnumerable<string> IModelExplicitDeclarationsHolder.GetSplitGroupsFor(System.Type type)
        {
            return declaredModel.GetSplitGroupsFor(type);
        }

        string IModelExplicitDeclarationsHolder.GetSplitGroupFor(MemberInfo member)
        {
            return declaredModel.GetSplitGroupFor(member);
        }

        void IModelExplicitDeclarationsHolder.AddAsRootEntity(System.Type type)
        {
            declaredModel.AddAsRootEntity(type);
        }

        void IModelExplicitDeclarationsHolder.AddAsComponent(System.Type type)
        {
            declaredModel.AddAsComponent(type);
        }

        void IModelExplicitDeclarationsHolder.AddAsTablePerClassEntity(System.Type type)
        {
            declaredModel.AddAsTablePerClassEntity(type);
        }

        void IModelExplicitDeclarationsHolder.AddAsTablePerClassHierarchyEntity(System.Type type)
        {
            declaredModel.AddAsTablePerClassHierarchyEntity(type);
        }

        void IModelExplicitDeclarationsHolder.AddAsTablePerConcreteClassEntity(System.Type type)
        {
            declaredModel.AddAsTablePerConcreteClassEntity(type);
        }

        void IModelExplicitDeclarationsHolder.AddAsOneToOneRelation(MemberInfo member)
        {
            declaredModel.AddAsOneToOneRelation(member);
        }

        void IModelExplicitDeclarationsHolder.AddAsManyToOneRelation(MemberInfo member)
        {
            declaredModel.AddAsManyToOneRelation(member);
        }

        void IModelExplicitDeclarationsHolder.AddAsManyToManyRelation(MemberInfo member)
        {
            declaredModel.AddAsManyToManyRelation(member);
        }

        void IModelExplicitDeclarationsHolder.AddAsOneToManyRelation(MemberInfo member)
        {
            declaredModel.AddAsOneToManyRelation(member);
        }

        void IModelExplicitDeclarationsHolder.AddAsManyToAnyRelation(MemberInfo member)
        {
            declaredModel.AddAsManyToAnyRelation(member);
        }

        void IModelExplicitDeclarationsHolder.AddAsAny(MemberInfo member)
        {
            declaredModel.AddAsAny(member);
        }

        void IModelExplicitDeclarationsHolder.AddAsPoid(MemberInfo member)
        {
            declaredModel.AddAsPoid(member);
        }

        void IModelExplicitDeclarationsHolder.AddAsVersionProperty(MemberInfo member)
        {
            declaredModel.AddAsVersionProperty(member);
        }

        void IModelExplicitDeclarationsHolder.AddAsNaturalId(MemberInfo member)
        {
            declaredModel.AddAsNaturalId(member);
        }

        void IModelExplicitDeclarationsHolder.AddAsSet(MemberInfo member)
        {
            declaredModel.AddAsSet(member);
        }

        void IModelExplicitDeclarationsHolder.AddAsBag(MemberInfo member)
        {
            declaredModel.AddAsBag(member);
        }

        void IModelExplicitDeclarationsHolder.AddAsIdBag(MemberInfo member)
        {
            declaredModel.AddAsIdBag(member);
        }

        void IModelExplicitDeclarationsHolder.AddAsList(MemberInfo member)
        {
            declaredModel.AddAsList(member);
        }

        void IModelExplicitDeclarationsHolder.AddAsArray(MemberInfo member)
        {
            declaredModel.AddAsArray(member);
        }

        void IModelExplicitDeclarationsHolder.AddAsMap(MemberInfo member)
        {
            declaredModel.AddAsMap(member);
        }

        void IModelExplicitDeclarationsHolder.AddAsProperty(MemberInfo member)
        {
            declaredModel.AddAsProperty(member);
        }

        void IModelExplicitDeclarationsHolder.AddAsPersistentMember(MemberInfo member)
        {
            declaredModel.AddAsPersistentMember(member);
        }

        void IModelExplicitDeclarationsHolder.AddAsPropertySplit(SplitDefinition definition)
        {
            declaredModel.AddAsPropertySplit(definition);
        }

        void IModelExplicitDeclarationsHolder.AddAsDynamicComponent(MemberInfo member, System.Type componentTemplate)
        {
            declaredModel.AddAsDynamicComponent(member, componentTemplate);
        }

        IEnumerable<MemberInfo> IModelExplicitDeclarationsHolder.ComposedIds
        {
            get { return declaredModel.ComposedIds; }
        }

        void IModelExplicitDeclarationsHolder.AddAsPartOfComposedId(MemberInfo member)
        {
            declaredModel.AddAsPartOfComposedId(member);
        }

        #endregion

        #region Implementation of IModelInspector

        bool IModelInspector.IsRootEntity(System.Type type)
        {
            bool declaredResult = declaredModel.RootEntities.Contains(type);
            return isRootEntity(type, declaredResult);
        }

        bool IModelInspector.IsComponent(System.Type type)
        {
            bool declaredResult = declaredModel.Components.Contains(type);
            return isComponent(type, declaredResult);
        }

        bool IModelInspector.IsEntity(System.Type type)
        {
            bool declaredResult = declaredModel.IsEntity(type);
            return isEntity(type, declaredResult);
        }

        bool IModelInspector.IsTablePerClass(System.Type type)
        {
            bool declaredResult = declaredModel.IsTablePerClass(type);
            return isTablePerClass(type, declaredResult);
        }

        bool IModelInspector.IsTablePerClassSplit(System.Type type, object splitGroupId, MemberInfo member)
        {
            bool declaredResult = declaredModel.IsTablePerClassSplit(type, splitGroupId, member);
            return isTablePerClassSplit(new SplitDefinition(type, splitGroupId.ToString(), member), declaredResult);
        }

        bool IModelInspector.IsTablePerClassHierarchy(System.Type type)
        {
            bool declaredResult = declaredModel.IsTablePerClassHierarchy(type);
            return isTablePerClassHierarchy(type, declaredResult);
        }

        bool IModelInspector.IsTablePerConcreteClass(System.Type type)
        {
            bool declaredResult = declaredModel.IsTablePerConcreteClass(type);
            return isTablePerConcreteClass(type, declaredResult);
        }

        bool IModelInspector.IsOneToOne(MemberInfo member)
        {
            bool declaredResult = DeclaredPolymorphicMatch(member, m => declaredModel.IsOneToOne(m));
            return isOneToOne(member, declaredResult);
        }

        bool IModelInspector.IsManyToOne(MemberInfo member)
        {
            bool declaredResult = DeclaredPolymorphicMatch(member, m => declaredModel.IsManyToOne(m));
            return isManyToOne(member, declaredResult);
        }

        bool IModelInspector.IsManyToMany(MemberInfo member)
        {
            bool declaredResult = DeclaredPolymorphicMatch(member, m => declaredModel.IsManyToMany(m));
            return isManyToMany(member, declaredResult);
        }

        bool IModelInspector.IsOneToMany(MemberInfo member)
        {
            bool declaredResult = DeclaredPolymorphicMatch(member, m => declaredModel.IsOneToMany(m));
            return isOneToMany(member, declaredResult);
        }

        bool IModelInspector.IsManyToAny(MemberInfo member)
        {
            bool declaredResult = DeclaredPolymorphicMatch(member, m => declaredModel.IsManyToAny(m));
            return isManyToAny(member, declaredResult);
        }

        bool IModelInspector.IsAny(MemberInfo member)
        {
            bool declaredResult = DeclaredPolymorphicMatch(member, m => declaredModel.IsAny(m));
            return isAny(member, declaredResult);
        }

        bool IModelInspector.IsPersistentId(MemberInfo member)
        {
            bool declaredResult = DeclaredPolymorphicMatch(member, m => declaredModel.IsPersistentId(m));
            return isPersistentId(member, declaredResult);
        }

        bool IModelInspector.IsMemberOfComposedId(MemberInfo member)
        {
            return declaredModel.IsMemberOfComposedId(member);
        }

        bool IModelInspector.IsVersion(MemberInfo member)
        {
            bool declaredResult = DeclaredPolymorphicMatch(member, m => declaredModel.IsVersion(m));
            return isVersion(member, declaredResult);
        }

        bool IModelInspector.IsMemberOfNaturalId(MemberInfo member)
        {
            bool declaredResult = DeclaredPolymorphicMatch(member, m => declaredModel.IsMemberOfNaturalId(m));
            return isMemberOfNaturalId(member, declaredResult);
        }

        bool IModelInspector.IsPersistentProperty(MemberInfo member)
        {
            bool declaredResult = DeclaredPolymorphicMatch(member, m => declaredModel.IsPersistentProperty(m));
            return isPersistentProperty(member, declaredResult);
        }

        bool IModelInspector.IsSet(MemberInfo role)
        {
            bool declaredResult = DeclaredPolymorphicMatch(role, m => declaredModel.IsSet(m));
            return isSet(role, declaredResult);
        }

        bool IModelInspector.IsBag(MemberInfo role)
        {
            bool declaredResult = DeclaredPolymorphicMatch(role, m => declaredModel.IsBag(m));
            return isBag(role, declaredResult);
        }

        bool IModelInspector.IsIdBag(MemberInfo role)
        {
            bool declaredResult = DeclaredPolymorphicMatch(role, m => declaredModel.IsIdBag(m));
            return isIdBag(role, declaredResult);
        }

        bool IModelInspector.IsList(MemberInfo role)
        {
            bool declaredResult = DeclaredPolymorphicMatch(role, m => declaredModel.IsList(m));
            return isList(role, declaredResult);
        }

        bool IModelInspector.IsArray(MemberInfo role)
        {
            bool declaredResult = DeclaredPolymorphicMatch(role, m => declaredModel.IsArray(m));
            return isArray(role, declaredResult);
        }

        bool IModelInspector.IsDictionary(MemberInfo role)
        {
            bool declaredResult = DeclaredPolymorphicMatch(role, m => declaredModel.IsDictionary(m));
            return isDictionary(role, declaredResult);
        }

        bool IModelInspector.IsProperty(MemberInfo member)
        {
            bool declaredResult = DeclaredPolymorphicMatch(member, m => declaredModel.IsProperty(m));
            return isProperty(member, declaredResult);
        }

        bool IModelInspector.IsDynamicComponent(MemberInfo member)
        {
            bool declaredResult = DeclaredPolymorphicMatch(member, m => declaredModel.IsDynamicComponent(m));
            return isDynamicComponent(member, declaredResult);
        }

        System.Type IModelInspector.GetDynamicComponentTemplate(MemberInfo member)
        {
            return declaredModel.GetDynamicComponentTemplate(member);
        }
        System.Type IModelExplicitDeclarationsHolder.GetDynamicComponentTemplate(MemberInfo member)
        {
            return declaredModel.GetDynamicComponentTemplate(member);
        }

        IEnumerable<string> IModelInspector.GetPropertiesSplits(System.Type type)
        {
            IEnumerable<string> declaredResult = declaredModel.GetPropertiesSplits(type);
            return splitsForType(type, declaredResult);
        }

        #endregion

        protected virtual bool DeclaredPolymorphicMatch(MemberInfo member, Func<MemberInfo, bool> declaredMatch)
        {
            return declaredMatch(member)
                         || member.GetMemberFromDeclaringClasses().Any(declaredMatch)
                         || member.GetPropertyFromInterfaces().Any(declaredMatch);
        }

        public void IsRootEntity(Func<System.Type, bool, bool> match)
        {
            if (match == null)
            {
                return;
            }
            isRootEntity = match;
        }

        public void IsComponent(Func<System.Type, bool, bool> match)
        {
            if (match == null)
            {
                return;
            }
            isComponent = match;
        }

        public void IsEntity(Func<System.Type, bool, bool> match)
        {
            if (match == null)
            {
                return;
            }
            isEntity = match;
        }

        public void IsTablePerClass(Func<System.Type, bool, bool> match)
        {
            if (match == null)
            {
                return;
            }
            isTablePerClass = match;
        }

        public void IsTablePerClassHierarchy(Func<System.Type, bool, bool> match)
        {
            if (match == null)
            {
                return;
            }
            isTablePerClassHierarchy = match;
        }

        public void IsTablePerConcreteClass(Func<System.Type, bool, bool> match)
        {
            if (match == null)
            {
                return;
            }
            isTablePerConcreteClass = match;
        }

        public void IsOneToOne(Func<MemberInfo, bool, bool> match)
        {
            if (match == null)
            {
                return;
            }
            isOneToOne = match;
        }

        public void IsManyToOne(Func<MemberInfo, bool, bool> match)
        {
            if (match == null)
            {
                return;
            }
            isManyToOne = match;
        }

        public void IsManyToMany(Func<MemberInfo, bool, bool> match)
        {
            if (match == null)
            {
                return;
            }
            isManyToMany = match;
        }

        public void IsOneToMany(Func<MemberInfo, bool, bool> match)
        {
            if (match == null)
            {
                return;
            }
            isOneToMany = match;
        }

        public void IsManyToAny(Func<MemberInfo, bool, bool> match)
        {
            if (match == null)
            {
                return;
            }
            isManyToAny = match;
        }

        public void IsAny(Func<MemberInfo, bool, bool> match)
        {
            if (match == null)
            {
                return;
            }
            isAny = match;
        }

        public void IsPersistentId(Func<MemberInfo, bool, bool> match)
        {
            if (match == null)
            {
                return;
            }
            isPersistentId = match;
        }

        public void IsVersion(Func<MemberInfo, bool, bool> match)
        {
            if (match == null)
            {
                return;
            }
            isVersion = match;
        }

        public void IsMemberOfNaturalId(Func<MemberInfo, bool, bool> match)
        {
            if (match == null)
            {
                return;
            }
            isMemberOfNaturalId = match;
        }

        public void IsPersistentProperty(Func<MemberInfo, bool, bool> match)
        {
            if (match == null)
            {
                return;
            }
            isPersistentProperty = match;
        }

        public void IsSet(Func<MemberInfo, bool, bool> match)
        {
            if (match == null)
            {
                return;
            }
            isSet = match;
        }

        public void IsBag(Func<MemberInfo, bool, bool> match)
        {
            if (match == null)
            {
                return;
            }
            isBag = match;
        }

        public void IsIdBag(Func<MemberInfo, bool, bool> match)
        {
            if (match == null)
            {
                return;
            }
            isIdBag = match;
        }

        public void IsList(Func<MemberInfo, bool, bool> match)
        {
            if (match == null)
            {
                return;
            }
            isList = match;
        }

        public void IsArray(Func<MemberInfo, bool, bool> match)
        {
            if (match == null)
            {
                return;
            }
            isArray = match;
        }

        public void IsDictionary(Func<MemberInfo, bool, bool> match)
        {
            if (match == null)
            {
                return;
            }
            isDictionary = match;
        }

        public void IsProperty(Func<MemberInfo, bool, bool> match)
        {
            if (match == null)
            {
                return;
            }
            isProperty = match;
        }

        public void SplitsFor(Func<System.Type, IEnumerable<string>, IEnumerable<string>> getPropertiesSplitsId)
        {
            if (getPropertiesSplitsId == null)
            {
                return;
            }
            splitsForType = getPropertiesSplitsId;
        }

        public void IsTablePerClassSplit(Func<SplitDefinition, bool, bool> match)
        {
            if (match == null)
            {
                return;
            }
            isTablePerClassSplit = match;
        }

        public void IsDynamicComponent(Func<MemberInfo, bool, bool> match)
        {
            if (match == null)
            {
                return;
            }
            isDynamicComponent = match;
        }
    }

    public static class DatabaseManager
    {
        private const string ChangeSnapshotIsolation = @"USE master  ALTER DATABASE {0} SET SINGLE_USER WITH ROLLBACK IMMEDIATE  ALTER DATABASE {0} SET ALLOW_SNAPSHOT_ISOLATION {1} ALTER DATABASE {0} SET READ_COMMITTED_SNAPSHOT {1}  ALTER DATABASE {0} SET MULTI_USER";

        private const string CreateDatabaseQuery = "USE master CREATE DATABASE {0} ON (NAME = {0}, FILENAME ='{1}{0}.mdf') LOG ON (NAME = {0}_log, FILENAME ='{1}{0}.ldf') COLLATE SQL_Latin1_General_CP1_CI_AS";

        private const string DefaultDataFilePathQuery = "SELECT SUBSTRING(physical_name, 1, CHARINDEX(N'master.mdf', LOWER(physical_name)) - 1) DataFileLocation FROM master.sys.master_files WHERE database_id = 1 AND FILE_ID = 1";

        private const string DeleteDatabaseQuery = "USE master ALTER DATABASE [{0}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE DROP DATABASE [{0}]";

        private const string DeleteDatabaseTables = @"use {0} begin tran DECLARE @TableName NVARCHAR(MAX) DECLARE @ConstraintName NVARCHAR(MAX) DECLARE DisableConstraints CURSOR FOR SELECT name as TABLE_NAME FROM sys.tables a WHERE name != 'Commits' and name != 'Snapshots'  OPEN DisableConstraints  FETCH NEXT FROM DisableConstraints INTO @TableName WHILE @@FETCH_STATUS = 0 BEGIN   EXEC('ALTER TABLE [' + @TableName + '] NOCHECK CONSTRAINT all')  FETCH NEXT FROM DisableConstraints INTO @TableName END print 'Done Disable Constraints' CLOSE DisableConstraints DEALLOCATE DisableConstraints DECLARE Constraints CURSOR FOR SELECT  TABLE_NAME, CONSTRAINT_NAME FROM INFORMATION_SCHEMA.CONSTRAINT_COLUMN_USAGE WHERE TABLE_NAME != 'Commits' and TABLE_NAME != 'Snapshots' OPEN Constraints FETCH NEXT FROM Constraints INTO @TableName, @ConstraintName WHILE @@FETCH_STATUS = 0 BEGIN EXEC('ALTER TABLE [' + @TableName + '] NOCHECK CONSTRAINT all') 		 if exists (SELECT	CONSTRAINT_NAME 	FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS 	WHERE CONSTRAINT_NAME = @ConstraintName 	and TABLE_NAME = @TableName)	BEGIN 		EXEC('ALTER TABLE [' + @TableName + '] DROP CONSTRAINT [' + @ConstraintName + ']')	END  FETCH NEXT FROM Constraints INTO @TableName, @ConstraintName END  CLOSE Constraints DEALLOCATE Constraints  print 'Done Constraints' DECLARE Tables CURSOR FOR  SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE (TABLE_NAME != 'Snapshots' and TABLE_NAME != 'Commits')  OPEN Tables FETCH NEXT FROM Tables INTO @TableName WHILE @@FETCH_STATUS = 0 BEGIN EXEC('DROP TABLE [' + @TableName + ']') FETCH NEXT FROM Tables INTO @TableName END CLOSE Tables DEALLOCATE Tables print 'Done Tables' commit tran";

        private const string IsDatabaseExistsQuery = "USE master SELECT name FROM master.dbo.sysdatabases WHERE name = N'{0}'";

        private const string TableExistsQuery = @"SELECT * FROM INFORMATION_SCHEMA.TABLES  WHERE TABLE_SCHEMA = 'dbo' AND  TABLE_NAME = '{0}'";

        public static bool CreateDatabase(string connectionString, string dataFilePath = "use_default", bool enableSnapshotIsolation = false)
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(connectionString);
            string dbName = builder.InitialCatalog;
            builder.InitialCatalog = "master";

            if (DatabaseExists(connectionString))
                throw new Exception(String.Format("Database '{0}' exists.", dbName));

            SqlConnection conn = new SqlConnection(builder.ToString());

            try
            {
                conn.Open();

                if (dataFilePath == "use_default")
                    dataFilePath = GetDefaultFilePath(conn);

                var command = String.Format(CreateDatabaseQuery, dbName, dataFilePath);
                SqlCommand cmd = new SqlCommand(command, conn);
                cmd.ExecuteNonQuery();
            }
            finally
            {
                conn.Close();
            }

            bool canConnectToDatabse = false;
            for (int i = 0; i < 200; i++)
            {
                if (!DatabaseManager.TryConnect(connectionString))
                    Thread.Sleep(100);
                else
                {
                    canConnectToDatabse = true;
                    break;
                }
            }
            if (canConnectToDatabse && enableSnapshotIsolation)
                EnableSnapshotIsolation(connectionString);

            return canConnectToDatabse;
        }

        public static void DeleteDatabase(string connectionString)
        {

            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(connectionString);
            string dbName = builder.InitialCatalog;
            builder.InitialCatalog = "master";
            if (!DatabaseExists(connectionString))
                return;

            SqlConnection conn = new SqlConnection(builder.ToString());

            try
            {
                conn.Open();
                var command = String.Format(DeleteDatabaseQuery, dbName);
                SqlCommand cmd = new SqlCommand(command, conn);
                cmd.ExecuteNonQuery();
            }
            finally
            {
                conn.Close();
            }
        }

        public static void DisableSnapshotIsolation(string connectionString)
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(connectionString);
            SqlConnection conn = new SqlConnection(builder.ToString());

            try
            {
                conn.Open();
                string query = String.Format(ChangeSnapshotIsolation, builder.InitialCatalog, "OFF");
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.ExecuteNonQuery();
            }
            finally
            {
                conn.Close();
            }
        }

        public static void DropTablesWithoutEventStore(string connectionString)
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(connectionString);
            string dbName = builder.InitialCatalog;
            builder.InitialCatalog = "master";
            if (!DatabaseExists(connectionString))
                return;

            SqlConnection conn = new SqlConnection(builder.ToString());

            try
            {
                conn.Open();
                var command = String.Format(DeleteDatabaseTables, dbName);

                SqlCommand cmd = new SqlCommand(command, conn);
                cmd.ExecuteNonQuery();
            }
            finally
            {
                conn.Close();
            }
        }

        public static void EnableSnapshotIsolation(string connectionString)
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(connectionString);
            SqlConnection conn = new SqlConnection(builder.ToString());

            try
            {
                conn.Open();
                string query = String.Format(ChangeSnapshotIsolation, builder.InitialCatalog, "ON");
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.ExecuteNonQuery();
            }
            finally
            {
                conn.Close();
            }
        }

        public static bool DatabaseExists(string connectionString)
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(connectionString);
            string dbName = builder.InitialCatalog;
            builder.InitialCatalog = "master";

            SqlConnection conn = new SqlConnection(builder.ToString());

            try
            {
                conn.Open();

                string query = String.Format(IsDatabaseExistsQuery, dbName);
                SqlCommand cmd = new SqlCommand(query, conn);
                SqlDataReader dr = cmd.ExecuteReader();
                using (dr)
                {
                    return dr.HasRows;
                }
            }
            finally
            {
                conn.Close();
            }
        }

        public static bool TableExists(string connectionString, string tableName)
        {
            bool exsists = false;
            SqlConnection conn = new SqlConnection(connectionString);
            try
            {
                conn.Open();

                var command = String.Format(TableExistsQuery, tableName.Replace("dbo.", ""));
                SqlCommand cmd = new SqlCommand(command, conn);
                var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    exsists = true;
                    break;
                }
                reader.Close();

            }
            finally
            {
                conn.Close();
            }
            return exsists;

        }

        private static string GetDefaultFilePath(SqlConnection conn)
        {
            SqlCommand pathCmd = new SqlCommand(DefaultDataFilePathQuery, conn);
            SqlDataReader dr = pathCmd.ExecuteReader();
            try
            {
                while (dr.Read())
                {
                    return dr["DataFileLocation"].ToString();
                }
                return "Cannot Find Default MSSQL database path.";
            }
            finally
            {
                dr.Close();
            }

        }

        static bool TryConnect(string connectionString)
        {
            bool isConnected = false;
            SqlConnection conn = new SqlConnection(connectionString);
            try
            {
                conn.Open();
                isConnected = true;
            }
            catch (SqlException)
            { }
            finally
            {
                conn.Close();
            }
            return isConnected;
        }

    }

}
