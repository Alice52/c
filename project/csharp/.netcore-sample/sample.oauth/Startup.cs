﻿using System;
using Edu.Ntu.Foundation.Core.Configurations;
using Edu.Ntu.Foundation.Core.Constants;
using Edu.Ntu.Foundation.Core.Extensions;
using Edu.Ntu.Foundation.Core.Filters;
using Edu.Ntu.Foundation.Core.Models;
using Edu.Ntu.Foundation.Core.Utils;
using Edu.Ntu.Oauth.Clients;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using sample.oauth.Services;
using RestSharp;
using Swashbuckle.AspNetCore.Swagger;

namespace sample.oauth
{
    public class Startup
    {
        private const string HTTPS_SERVICE_URL = "https://localhost:44386/";
        private const string HTTP_SERVICE_URL = "http://localhost:8005/";
        private const string jwksPath = "/oauth2/jwks";
        private readonly ILogger<Startup> _logger;
        public IConfiguration Configuration { get; }
        public SwaggerConfig SwaggerConfig { get; set; }
        public JwtBearerConfig JwtBearerConfig { get; set; }
        public IConfiguration JwtConfiguration { get; set; }

        public Startup(ILogger<Startup> logger, IConfiguration configuration, IHostingEnvironment env)
        {
            _logger = logger;
            Configuration = configuration;
            SwaggerConfig = new SwaggerConfig();
            Configuration.GetSection("SwaggerConfig").Bind(SwaggerConfig);
            JwtBearerConfig = new JwtBearerConfig();
            Configuration.GetSection("JwtBearerConfig").Bind(JwtBearerConfig);
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            try
            {
                InjectSwagger(services);
                InjectConfigs(services);
                InjectServices(services);
                InjectClients(services);
                InjectNamedClients(services);
                ConfigureAuthentication(services);
                services.AddMvc(options =>
                    {
                        options.Filters.Add(typeof(ExceptionFilter));
                    })
                    .SetCompatibilityVersion(CompatibilityVersion.Version_2_2).AddJsonOptions(options =>
                    {
                        options.SerializerSettings.ContractResolver =
                            new CamelCasePropertyNamesContractResolver();

                        options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Serialize;
                    });

                services.ConfigureInvalidModelStateResponseApiBehavior();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Configure sample.oauth Background Service failed.");
            }

        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            try
            {
                if (env.IsDevelopment())
                {
                    app.UseDeveloperExceptionPage();
                }
                // Notice: this should be placed before UseMvc
                app.UseAuthentication();
                app.UseMvc();

                // Swagger UI
                if (SwaggerConfig.IsEnabled)
                {
                    app.UseSwagger().UseSwaggerUI(c =>
                    {
                        c.SwaggerEndpoint("/swagger/v1/swagger.json", "sample.oauth background API");
                    });
                }
                app.UseStaticFiles();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Start up sample.oauth Background Service failed.");
            }
        }

        private void InjectClients(IServiceCollection services)
        {
            services.AddTransient<IJwtTokenClient, JwtTokenClient>();
        }

        public void InjectNamedClients(IServiceCollection services)
        {
            services.AddHttpClient(ClientsConstants.TABLE_SERVICE_CLIENT, client =>
            {
                client.BaseAddress = new Uri(HTTPS_SERVICE_URL);
                client.Timeout = TimeSpan.FromMilliseconds(1000);

                var client2 = new RestClient(HTTPS_SERVICE_URL);
                client2.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
                var request = new RestRequest("/OauthService/GetAccessToken", Method.POST);
                var jWTToken = JsonUtil.DeserializeObject<JwtTokenModel>(client2.Execute(request).Content);
                client.DefaultRequestHeaders.Add(RequestHeaders.AUTHORIZATION, jWTToken.TokenType + RequestHeaders.WHITESPACE + jWTToken.AccessToken);
            });
        }


        private void InjectSwagger(IServiceCollection services)
        {
            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new Info
                {
                    Title = "sample.oauth API",
                    Version = "v1"
                });
            });
        }

        private void InjectConfigs(IServiceCollection services)
        {
            JwtConfiguration = Configuration.GetSection("JwtConfiguration");
            services.Configure<JwtConfiguration>(Configuration.GetSection("JwtConfiguration"));
        }

        private void InjectServices(IServiceCollection services)
        {
            services.AddTransient<IOauthService, OauthService>();
        }

        private void ConfigureAuthentication(IServiceCollection services)
        {
            var client = new RestClient(JwtBearerConfig.BaseUrl);
            client.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
            var request = new RestRequest(jwksPath, Method.GET);
            var jwtKey = client.Execute(request).Content;
            var Ids4keys = JsonConvert.DeserializeObject<Ids4Keys>(jwtKey);
            var jwk = Ids4keys.keys;

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                    .AddJwtBearer(options =>
                    {
                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = JwtBearerConfig.ValidateIssuer,
                            ValidateIssuerSigningKey = JwtBearerConfig.ValidateIssuerSigningKey,
                            IssuerSigningKeys = jwk,
                            ValidateAudience = JwtBearerConfig.ValidateAudience,
                            ValidAudience = JwtBearerConfig.ValidAudience,
                            ValidateLifetime = JwtBearerConfig.ValidateLifetime,
                            RequireExpirationTime = JwtBearerConfig.RequireExpirationTime
                        };
                    });
        }
    }


    public class Ids4Keys
    {
        public JsonWebKey[] keys { get; set; }
    }
}
