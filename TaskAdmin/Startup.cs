﻿using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using TaskAdmin.Filters;
using TaskAdmin.Libraries;
using TaskAdmin.Subscribes;

namespace TaskAdmin
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }


        public void ConfigureServices(IServiceCollection services)
        {

            //为各数据库注入连接字符串
            Repository.Database.dbContext.ConnectionString = Configuration.GetConnectionString("dbConnection");
            services.AddDbContextPool<Repository.Database.dbContext>(options => { }, 100);

            services.AddSingleton<DemoSubscribe>();
            services.AddCap(options =>
            {

                //使用 Redis 传输消息
                options.UseRedis(Configuration.GetConnectionString("redisConnection"));

                //使用 RabbitMQ 传输消息
                //options.UseRabbitMQ(options =>
                //{
                //    options.HostName = "";
                //    options.UserName = "";
                //    options.Password = "";
                //    options.VirtualHost = "";
                //    //options.Port = 5671;
                //    options.ConnectionFactoryOptions = options =>
                //    {
                //        options.Ssl = new RabbitMQ.Client.SslOption { Enabled = true, ServerName = "" };
                //    };
                //});


                //使用 ef 搭配 db 存储执行情况
                options.UseEntityFramework<Repository.Database.dbContext>();

                options.UseDashboard();
                options.JsonSerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);

                options.DefaultGroupName = "default";   //默认组名称
                options.GroupNamePrefix = null; //全局组名称前缀
                options.TopicNamePrefix = null; //Topic 统一前缀
                options.Version = "v1";
                options.FailedRetryInterval = 60;   //失败时重试间隔
                options.ConsumerThreadCount = 1;    //消费者线程并行处理消息的线程数，当这个值大于1时，将不能保证消息执行的顺序
                options.FailedRetryCount = 10;  //失败时重试的最大次数
                options.FailedThresholdCallback = null; //重试阈值的失败回调
                options.SucceedMessageExpiredAfter = 24 * 3600; //成功消息的过期时间（秒）
            }).AddSubscribeFilter<CapSubscribeFilter>();


            services.AddHsts(options =>
            {
                options.MaxAge = TimeSpan.FromDays(365);
            });


            //注册 HangFire(Memory)
            services.AddHangfire(configuration => configuration.UseInMemoryStorage());


            //注册 HangFire(Redis)
            //services.AddHangfire(configuration => configuration.UseRedisStorage(Configuration.GetConnectionString("hangfireConnection")));


            //注册 HangFire(SqlServer)
            //services.AddHangfire(configuration => configuration
            //    .UseSqlServerStorage(Configuration.GetConnectionString("hangfireConnection"), new SqlServerStorageOptions
            //    {
            //        SchemaName = "hangfire"
            //    }));


            //注册 HangFire(PostgreSQL)
            //services.AddHangfire(configuration => configuration
            //    .UsePostgreSqlStorage(Configuration.GetConnectionString("hangfireConnection"), new PostgreSqlStorageOptions
            //    {
            //        SchemaName = "hangfire"
            //    }));


            //注册 HangFire(MySql)
            //services.AddHangfire(configuration => configuration
            //    .UseStorage(new MySqlStorage(Configuration.GetConnectionString("hangfireConnection") + "Allow User Variables=True", new MySqlStorageOptions
            //    {
            //        TablesPrefix = "hangfire_"
            //    })));



            // 注册 HangFire 服务
            services.AddHangfireServer(config => config.SchedulePollingInterval = TimeSpan.FromSeconds(3));



            services.AddControllersWithViews();


            //注册HttpContext
            Libraries.Http.HttpContext.Add(services);


            //注册全局过滤器
            services.AddMvc(config => config.Filters.Add(new GlobalFilter()));



            //注册配置文件信息
            Libraries.Start.StartConfiguration.Add(Configuration);


            //托管Session到Redis中
            if (Convert.ToBoolean(Configuration["SessionToRedis"]))
            {
                services.AddDistributedRedisCache(options =>
                {
                    options.Configuration = Configuration.GetConnectionString("redisConnection");
                });
            }


            //注册Session
            services.AddSession(options =>
            {
                //设置Session过期时间
                options.IdleTimeout = TimeSpan.FromHours(3);
            });


            //解决中文被编码
            services.AddSingleton(HtmlEncoder.Create(UnicodeRanges.All));


            //注册统一模型验证
            services.Configure<ApiBehaviorOptions>(options =>
            {
                options.InvalidModelStateResponseFactory = actionContext =>
                {

                    //获取验证失败的模型字段 
                    var errors = actionContext.ModelState.Where(e => e.Value.Errors.Count > 0).Select(e => e.Value.Errors.First().ErrorMessage).ToList();

                    var dataStr = string.Join(" | ", errors);

                    //设置返回内容
                    var result = new
                    {
                        errMsg = dataStr
                    };

                    return new BadRequestObjectResult(result);
                };
            });


            //注册雪花ID算法示例
            services.AddSingleton(new Common.SnowflakeHelper(0, 0));

        }



        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {


            //设置本地化信息，可实现 固定 Hangfire 管理面板为中文显示
            app.UseRequestLocalization(new RequestLocalizationOptions
            {
                DefaultRequestCulture = new RequestCulture("zh-CN"),
                SupportedCultures = new[]
                {
                    new CultureInfo("zh-CN")
                },
                SupportedUICultures = new[]
                {
                    new CultureInfo("zh-CN")
                }
            });



            //开启倒带模式运行多次读取HttpContext.Body中的内容
            app.Use(next => context =>
            {
                context.Request.EnableBuffering();
                return next(context);
            });


            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                //注册全局异常处理机制
                app.UseExceptionHandler(builder => builder.Run(async context => await GlobalError.ErrorEvent(context)));
            }


            app.UseHsts();


            //强制重定向到Https
            app.UseHttpsRedirection();


            app.UseStaticFiles();


            //注册HttpContext
            Libraries.Http.HttpContext.Initialize(app, env);


            //注册Session
            app.UseSession();


            //注册HostingEnvironment
            Libraries.Start.StartHostingEnvironment.Add(env);


            app.UseRouting();


            app.UseHangfireDashboard("/hangfire", new DashboardOptions
            {
                Authorization = new[] { new DashboardAuthorizationFilter() },
                DisplayStorageConnectionString = false
            });


            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
            });

            Tasks.Main.Run();

        }
    }
}
