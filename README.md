## About The Project

DDNS for NameSilo registrar based on .net core 5.0 with docker support.
Dynamically updates domain record when your dynamic IP is changed. 
Supported platforms:
- linux/amd64
- linux/arm32v7 (RaspberryPi)

Uses `https://myexternalip.com/` to obtain current IP address.


### Docker images
You can use existing docker images:
- linux/amd64: aderesh/namesiloddns:latest
- linux/arm32: aderesh/namesiloddns:latest-armv7

### Changelog
## 0.1.0 - 22.10.2023
- Updated README.MD with NAMESILO_HOST_REGEX info to avoid confusion because of NAMESILO_DOMAIN
- If current IP address is IPv4, only A records are updated. If it's IPv6, only AAAA records are updated. 
- add more logs around updated/skipped records
- Updated to .NET 6 LTS

## 0.0.2 - 23.10.2023
- add version and include it in docker tag

## 12.10.2022
- add NAMESILO_HOST_REGEX to update multiple host records. It could update example.com as well as blog.example.com, www.example.com etc
- setting NAMESILO_DELAY to "00:00:00" would force the app to exit after one update. This is useful to schedule this as a cron job
- added more logs
## Initial


### Prerequisites
#### Obtain NameSilo API key
API key is needed to update DNS records using NameSilo API. The key should be passed as a `NAMESILO_APIKEY` environment variable.
You can skip this section if you have existing API key.

To generate new key
* open https://www.namesilo.com/account/api-manager
* under 'API Key' section, press 'Generate'

#### To use as a docker image
Install latest docker

#### To use as a console app
Install .net core sdk 5.0. 

## Getting Started

You can either use this as a docker container or you can run it as a console app. 

Environment variables:
* NAMESILO_DOMAIN - (REQUIRED) Domain name. For instance, 'example.org'. Cannot be used with NAMESILO_HOST_REGEX
* NAMESILO_HOST - (OPTIONAL) Short host name. Default is ''. If your record is 'blog.exmaple.org', your host name is 'blog'. Cannot be used with NAMESILO_HOST_REGEX
* NAMESILO_HOST_REGEX - (REQUIRED) Full host name.  If your record is 'blog.exmaple.org', your host name is 'blog.exmaple.org'.
* NAMESILO_APIKEY - (REQUIRED) NameSilo API key. 
* NAMESILO_DELAY - (OPTIONAL) Requests delay. By default, polls server every 5 minutes.

### To launch using docker

#### Build
```ps
docker build -t namesiloddns .
```

#### Run
one run
```
docker run -d -e "NAMESILO_DOMAIN=example.com" -e "NAMESILO_HOST=blog" -e "NAMESILO_APIKEY=YourNameSiloAPIKey" -e "NAMESILO_DELAY=00:01:00" --name namesiloddns namesiloddns
```

automatically run on reboot
```
 docker run -d -e "NAMESILO_DOMAIN=example.com" -e "NAMESILO_HOST=blog" -e "NAMESILO_APIKEY=YourNameSiloAPIKey" -e "NAMESILO_DELAY=00:01:00" --restart always --name namesiloddns aderesh/namesiloddns:latest-arm32v7
```

### To launch as a console app

#### Build
`dotnet restore`

#### Run (Windows)
```ps
$env:NAMESILO_DOMAIN="example.com"
$env:NAMESILO_HOST="blog"
$env:NAMESILO_APIKEY="YourNameSiloAPIKey"
$env:NAMESILO_DELAY="00:03:00"

dotnet run
```

## Contributing

1. Fork the Project
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3. Commit your Changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the Branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## License

Distributed under the MIT License. 
