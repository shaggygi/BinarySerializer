﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace BinarySerialization.Graph
{
    internal class EnumNode : ValueNode
    {
        private SerializedType _serializedType = SerializedType.Default;
        private IDictionary<Enum, string> _enumValues;
        private IDictionary<string, Enum> _valueEnums; 
        private Type _underlyingType;
        private int? _enumValueLength;

        public EnumNode(Node parent, Type type) : base(parent, type)
        {
            InitializeEnumValues();
        }

        public EnumNode(Node parent, MemberInfo memberInfo) : base(parent, memberInfo)
        {
            InitializeEnumValues();
        }

        public override void SerializeOverride(Stream stream)
        {
            var value = _enumValues != null ? _enumValues[(Enum)BoundValue] : BoundValue;
            Serialize(stream, value, _serializedType, _enumValueLength);
        }

        public override void DeserializeOverride(StreamLimiter stream)
        {
            var value = Deserialize(stream, _serializedType, _enumValueLength);

            if (_valueEnums != null)
            {
                value = _valueEnums[(string)value];
            }

            Func<object, object> converter;
            var underlyingValue = TypeConverters.TryGetValue(_underlyingType, out converter) ? converter(value) : value;
            Value = Enum.ToObject(Type, underlyingValue);
        }

        private void InitializeEnumValues()
        {
            var serializedType = GetSerializedType();

            if (serializedType != SerializedType.Default && serializedType != SerializedType.LengthPrefixedString &&
                serializedType != SerializedType.NullTerminatedString && serializedType != SerializedType.SizedString)
                return;

            var values = Enum.GetValues(Type).Cast<Enum>();

            /* Get enum attributes */
            var enumAttributes = values.ToDictionary(value => value, value =>
            {
                var memberInfo = Type.GetMember(value.ToString()).Single();
                return (SerializeAsEnumAttribute) memberInfo.GetCustomAttributes(
                    typeof (SerializeAsEnumAttribute),
                    false).FirstOrDefault();
            });

            /* If any are specified, build dictionary of them one time */
            if (enumAttributes.Any(enumAttribute => enumAttribute.Value != null))
            {
                _enumValues = enumAttributes.ToDictionary(enumAttribute => enumAttribute.Key,
                    enumAttribute =>
                    {
                        var attribute = enumAttribute.Value;
                        return attribute != null && attribute.Value != null ? attribute.Value : enumAttribute.Key.ToString();
                    });

                _valueEnums = _enumValues.ToDictionary(enumValue => enumValue.Value, enumValue => enumValue.Key);

                var lengthGroups =
                    _enumValues.Where(enumValue => enumValue.Value != null)
                        .Select(enumValue => enumValue.Value)
                        .GroupBy(value => value.Length).ToList();

                /* If the type isn't specified, let's try to guess it smartly */
                if (serializedType == SerializedType.Default)
                {
                    /* If everything is the same length, assume fixed length */
                    if (lengthGroups.Count == 1)
                    {
                        _serializedType = SerializedType.SizedString;
                        _enumValueLength = lengthGroups[0].Key;
                    }
                    else _serializedType = SerializedType.NullTerminatedString;
                }
                else if (serializedType == SerializedType.SizedString)
                {
                    /* If fixed size is specified, get max length in order to accomodate all values */
                    _enumValueLength = lengthGroups[0].Max(lengthGroup => lengthGroup.Length);
                }
            }

            /* If a field length is specified to be less than the max enum value length, we can't reliably recover the enum
             * values on deserialization. */
            if (_enumValueLength != null && FieldLengthBinding != null && FieldLengthBinding.IsConst)
            {
                if ((int)FieldLengthBinding.Value < _enumValueLength.Value)
                    throw new InvalidOperationException("Field length cannot be less than max enum name length.");
            }

            _underlyingType = Enum.GetUnderlyingType(Type);

            if (_serializedType == SerializedType.Default)
            {
                _serializedType = GetSerializedType(_underlyingType);
            }
        }
    }
}