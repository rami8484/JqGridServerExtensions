using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq.Dynamic;
using System.Data.Entity.Core.Objects;
using System.Linq.Expressions;
using System.Reflection;
using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Data.Entity;
using System.Data.Entity.SqlServer;

namespace ProcessAPI.Helpers
{
    public class ExpressionHelper<TEntity> where TEntity : IDBBaseEntity
    {
        public static IQueryable<TEntity> search(IQueryable<TEntity> queryToSearchIn, string filters) // Simply input your Queryable object and the filters json string comming from JqGrid and it filters at the database level
        {
            if (string.IsNullOrEmpty(filters))
            {
                return queryToSearchIn;
            }

            return queryToSearchIn.Where(buildExpression(filters));
        }

        public static IQueryable<TEntity> orderByExpression(IQueryable<TEntity> query, string orderByColumn, string sord) // auto detect the datatype at the database level and sort according it.
        {
            var parameter = Expression.Parameter(typeof(TEntity));

            var member = typeof(TEntity).GetProperty(orderByColumn);

            var memberAccessExpression = Expression.MakeMemberAccess(parameter, member);

            var lambdaExpression = Expression.Lambda(memberAccessExpression, parameter);
            
            var callExpression = Expression.Call(typeof(Queryable), sord == "asc" ? "OrderBy" : "OrderByDescending"
                , new Type[] { typeof(TEntity), member.PropertyType },
               query.Expression, Expression.Quote(lambdaExpression));

            return query.Provider.CreateQuery<TEntity>(callExpression);
        }
        private static Expression<Func<TEntity, bool>> buildExpression(string filters)
        {


            JObject jobject = JObject.Parse(filters);

            JToken groupOp = (JToken)jobject["groupOp"];

            string groupOpeString = "";

            groupOpeString = groupOp.Value<string>() == "AND" ? " && " :
                groupOp.Value<string>() == "OR" ? " || " : " ! ";

            JArray rules = (JArray)jobject["rules"];

            Expression resultExpression = Expression.Constant(true);

            var parameter = Expression.Parameter(typeof(TEntity));

            foreach (var rule in rules)
            {

                var entityPropertyType = typeof(TEntity).GetProperties().Where(x => x.Name == rule.Value<string>("field")).ToList()[0].PropertyType;

                Expression expression = null;

                if ((entityPropertyType == typeof(DateTime) || entityPropertyType == typeof(Nullable<DateTime>)
                    || entityPropertyType == typeof(Nullable<DateTime>) )&& rule.Value<string>("op") != "cn")
                {
                    var typeConverter = TypeDescriptor.GetConverter(entityPropertyType);

                    var convertedValue = (DateTime?)(typeConverter.ConvertFrom(rule.Value<string>("data")));//, typeof(TEntity).GetProperties().Where(x => x.Name == rule.Value<string>("field")).ToList()[0].PropertyType);

                    var member = Expression.Property(parameter, rule.Value<string>("field"));

                    var constant = Expression.Constant(convertedValue, entityPropertyType);
                   
                    var convertedConstant = Expression.Convert(constant, typeof(Nullable<DateTime>));
                    
                    var convertedMember = Expression.Convert(member, typeof(Nullable<DateTime>));
                    
                    MethodInfo truncateTimeMethodInfo = typeof(DbFunctions).GetMethod("TruncateTime", new Type[1] { typeof(Nullable<DateTime>) });
                    
                    var memberCall = Expression.Call(truncateTimeMethodInfo, convertedMember);

                    switch (rule.Value<string>("op"))
                    {
                        case "eq":
                            expression = Expression.Equal(memberCall, convertedConstant);
                            break;
                        case "ne":
                            expression = Expression.NotEqual(memberCall, convertedConstant);
                            break;
                        case "gt":
                            expression = Expression.GreaterThan(memberCall, convertedConstant);
                            break;
                        case "ge":
                            expression = Expression.GreaterThanOrEqual(memberCall, convertedConstant);
                            break;
                        case "lt":
                            expression = Expression.LessThan(memberCall, convertedConstant);
                            break;
                        case "le":
                            expression = Expression.LessThanOrEqual(memberCall, convertedConstant);
                            break;
                        default:
                            expression = Expression.Equal(member, convertedConstant);
                            break;
                    }
                }
                else
                {
                    var typeConverter = TypeDescriptor.GetConverter(rule.Value<string>("op") == "cn"? typeof(string) : entityPropertyType);

                    var convertedValue = typeConverter.ConvertFrom(rule.Value<string>("data"));//, typeof(TEntity).GetProperties().Where(x => x.Name == rule.Value<string>("field")).ToList()[0].PropertyType);

                    var member = Expression.Property(parameter, rule.Value<string>("field"));

                    var constant = Expression.Constant(convertedValue, rule.Value<string>("op") == "cn" ? typeof(string) : entityPropertyType);

                    switch (rule.Value<string>("op"))
                    {
                        case "eq":
                            expression = Expression.Equal(member, constant);
                            break;
                        case "ne":
                            expression = Expression.NotEqual(member, constant);
                            break;
                        case "gt":
                            expression = Expression.GreaterThan(member, constant);
                            break;
                        case "ge":
                            expression = Expression.GreaterThanOrEqual(member, constant);
                            break;
                        case "lt":
                            expression = Expression.LessThan(member, constant);
                            break;
                        case "le":
                            expression = Expression.LessThanOrEqual(member, constant);
                            break;
                        case "cn": ////// important: please dont use datetime columns in Contains method, disable يحتوي in jqgrid for datetime columns
                            
                            if(entityPropertyType == typeof(string))
                            {
                                var convertedMember = Expression.Convert(member, entityPropertyType);
                                
                                MethodInfo containsMethodInfo = typeof(string).GetMethod("Contains", new Type[1] { typeof(string) });
                                var stringifiedConstant = Expression.Convert(constant, typeof(string));


                                expression = Expression.Call(convertedMember,
                                    containsMethodInfo,
                                    stringifiedConstant);

                                expression = Expression.Equal(expression, Expression.Constant(true));
                                
                            }
                            else
                            {
                                var ifNotNullThenNullable = Nullable.GetUnderlyingType(entityPropertyType) == null ?
                                typeof(Nullable<>).MakeGenericType(entityPropertyType) : entityPropertyType;

                                var convertedMember = Expression.Convert(member, ifNotNullThenNullable);
                                var stringConverterMethodInfo = typeof(SqlFunctions).GetMethod("StringConvert", new Type[1] { ifNotNullThenNullable });

                                var stringConverterMethodCall = Expression.Call(stringConverterMethodInfo, convertedMember);

                                MethodInfo containsMethodInfo = typeof(string).GetMethod("Contains", new Type[1] { typeof(string) });
                                var stringifiedConstant = Expression.Convert(constant, typeof(string));

                                Expression expressionInstance = null;

                                if (ifNotNullThenNullable == typeof(string))
                                {
                                    expressionInstance = convertedMember;
                                }
                                else
                                {
                                    expressionInstance = stringConverterMethodCall;
                                }

                                expression = Expression.Call(expressionInstance,
                                    containsMethodInfo,
                                    stringifiedConstant);

                                expression = Expression.Equal(expression, Expression.Constant(true));
                                
                            }
                            break;

                        default:
                            expression = Expression.Equal(member, constant);
                            break;
                    }


                }

                //resultExpression = Expression.AndAlso(resultExpression, expression);

                resultExpression = combineExpression(resultExpression, expression, groupOp.Value<string>());
            }


            return Expression.Lambda<Func<TEntity, bool>>(resultExpression, parameter);
        }

        private static Expression combineExpression(Expression exp1, Expression exp2, string logicalOperation)
        {
            BinaryExpression resultExp = null;

            if (logicalOperation == "AND")
            {

                resultExp = Expression.AndAlso(exp1, exp2);

            }
            else if (logicalOperation == "OR")
            {
                resultExp = Expression.OrElse(exp1, exp2);
            }


            //var s = Expression.Lambda<Func<TEntity, bool>>(Expression.AndAlso(exp1, exp2), Parameter);

            return resultExp;
        }


    }

}