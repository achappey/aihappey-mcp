# aihappey-mcp

Open-source **MCP backend** that hosts and routes a large catalog of AI and business integrations as Model Context Protocol servers.

---

## What this project is

`aihappey-mcp` is the backend layer in the AIHappey ecosystem for MCP.
It provides a single place to expose many MCP servers, from AI providers to Microsoft 365, public datasets, and operational tools.

## What you can do with it

- Connect one MCP endpoint and discover many available servers.
- Use server-based tools for models, search, files, media, and workflows.
- Combine static JSON-defined servers with SQL-backed dynamic servers.
- Run with either header-based auth or Azure-authenticated hosting samples.
- Reuse the same backend across different clients and agent experiences.

## MCP server catalog at a glance

This repository includes **336 MCP server definitions** from [`Servers/MCPhappey.Servers.JSON/Servers`](Servers/MCPhappey.Servers.JSON/Servers).

The logo wall below is **generated from those `Server.json` files**, deduplicated by `serverInfo.websiteUrl`.

- **Unique logo domains shown:** 122
- **Total server definitions:** 336
- Servers without `websiteUrl` or icon are not shown in the wall, but are included in the total count.

<!-- PROVIDER_LOGO_GRID_START -->
<p>
<a href="https://302.ai" title="302.AI 3D Modelling" target="_blank" rel="noopener noreferrer"><img src="https://302.ai/img/logo.png" alt="302.AI 3D Modelling" width="28" height="28" /></a>
<a href="https://aimlapi.com" title="AI/ML Audio" target="_blank" rel="noopener noreferrer"><img src="https://media.licdn.com/dms/image/v2/D560BAQFF5cVF9c_cOw/company-logo_200_200/company-logo_200_200/0/1709201452469/aimlapi_logo?e=2147483647&amp;v=beta&amp;t=l2fmaW9qdhOZ9wR3sukZpFYETyNGEA5jatU66ECxdFQ" alt="AI/ML Audio" width="28" height="28" /></a>
<a href="https://www.anthropic.com" title="Anthropic Code Execution" target="_blank" rel="noopener noreferrer"><img src="https://upload.wikimedia.org/wikipedia/commons/1/14/Anthropic.png" alt="Anthropic Code Execution" width="28" height="28" /></a>
<a href="https://www.assemblyai.com" title="AssemblyAI" target="_blank" rel="noopener noreferrer"><img src="https://yt3.googleusercontent.com/5z_-jPDKLrUlaxA0Ow7BRdIAwbh6YQYrqU3pd8Cm6okahuJ3BaawiEPpdWUhwE98v_j3ugUAbA=s900-c-k-c0x00ffffff-no-rj" alt="AssemblyAI" width="28" height="28" /></a>
<a href="https://async.ai" title="asyncAI" target="_blank" rel="noopener noreferrer"><img src="https://media.licdn.com/dms/image/v2/D4E0BAQH8IAPHkrLBIA/company-logo_200_200/company-logo_200_200/0/1738686836840/async_ai_logo?e=2147483647&amp;v=beta&amp;t=xzU-nIDFl9Fvyye-7Ki46HPuZyq1ZKxbnFjexRjFbK4" alt="asyncAI" width="28" height="28" /></a>
<a href="https://audixa.ai" title="Audixa" target="_blank" rel="noopener noreferrer"><img src="https://cdn.audixa.ai/brand.png" alt="Audixa" width="28" height="28" /></a>
<a href="https://azure.microsoft.com/en-us/products/ai-services/ai-document-intelligence" title="Azure Document Intelligence" target="_blank" rel="noopener noreferrer"><img src="https://upload.wikimedia.org/wikipedia/commons/thumb/f/fa/Microsoft_Azure.svg/2048px-Microsoft_Azure.svg.png" alt="Azure Document Intelligence" width="28" height="28" /></a>
<a href="https://portal.azure.com" title="Azure Service Management" target="_blank" rel="noopener noreferrer"><img src="https://upload.wikimedia.org/wikipedia/commons/thumb/f/fa/Microsoft_Azure.svg/2048px-Microsoft_Azure.svg.png" alt="Azure Service Management" width="28" height="28" /></a>
<a href="https://www.kadaster.nl/zakelijk/registraties/basisregistraties/bag" title="Basisregistratie Adressen en Gebouwen (BAG)" target="_blank" rel="noopener noreferrer"><img src="https://www.nordichq.com/wp-content/uploads/2023/03/kvk-logo.jpg" alt="Basisregistratie Adressen en Gebouwen (BAG)" width="28" height="28" /></a>
<a href="https://berget.ai" title="Berget AI Models" target="_blank" rel="noopener noreferrer"><img src="https://media.licdn.com/dms/image/v2/D4D0BAQHQGuTgU7ix9w/company-logo_200_200/B4DZWbcxfDGkAI-/0/1742069766482/bergetai_logo?e=2147483647&amp;v=beta&amp;t=EkSpekwlwpXxB9L4u92NEy8s1jqTJaxMnEaSTr1tfZA" alt="Berget AI Models" width="28" height="28" /></a>
<a href="https://brave.com" title="Brave" target="_blank" rel="noopener noreferrer"><img src="https://upload.wikimedia.org/wikipedia/commons/5/51/Brave_icon_lionface.png" alt="Brave" width="28" height="28" /></a>
<a href="https://bria.ai" title="Bria" target="_blank" rel="noopener noreferrer"><img src="https://media.licdn.com/dms/image/v2/D560BAQE49KK-YdA1Sw/company-logo_200_200/B56ZgemHKHHMAM-/0/1752859967087/briaai_logo?e=2147483647&amp;v=beta&amp;t=RCAM92DfkmxueBQG_92cZNWumsoBu5BHrCSBBX1vQak" alt="Bria" width="28" height="28" /></a>
<a href="https://brightdata.com/" title="BrightData" target="_blank" rel="noopener noreferrer"><img src="https://www.nbn.org.il/jobboard/wp-content/uploads/2023/11/Bright-Data-Logo.jpeg" alt="BrightData" width="28" height="28" /></a>
<a href="https://calcasa.nl" title="Calcasa" target="_blank" rel="noopener noreferrer"><img src="https://pbs.twimg.com/profile_images/1374711327013416962/4Uq38c5o_400x400.jpg" alt="Calcasa" width="28" height="28" /></a>
<a href="https://www.capitalvalue.nl" title="Capital Value" target="_blank" rel="noopener noreferrer"><img src="https://media.licdn.com/dms/image/v2/D4E0BAQH5DgNT3NJoxw/company-logo_200_200/company-logo_200_200/0/1719257402788/capital_value_b_v__logo?e=2147483647&amp;v=beta&amp;t=6RwkZ2q8UI1KDk6YnXPPXpgKhAah_s1uZbFGRM7zGRI" alt="Capital Value" width="28" height="28" /></a>
<a href="https://www.cbs.nl" title="Centraal Bureau voor de Statistiek (CBS)" target="_blank" rel="noopener noreferrer"><img src="https://media.licdn.com/dms/image/v2/D4E0BAQE_ZZXTkF55Cw/company-logo_200_200/company-logo_200_200/0/1730379746082/centraal_bureau_voor_de_statistiek_logo?e=2147483647&amp;v=beta&amp;t=bnlH9Shb1FZbT_ruxAp4_XlRgd6KD5fw2Wvp5bXRnZw" alt="Centraal Bureau voor de Statistiek (CBS)" width="28" height="28" /></a>
<a href="https://coelo.nl" title="Centrum Onderzoek Economie Lagere Overheden" target="_blank" rel="noopener noreferrer"><img src="https://www.rug.nl/about-ug/practical-matters/huisstijl/logobank-new/socialemedia/twitter_icon_300px_rood.jpg" alt="Centrum Onderzoek Economie Lagere Overheden" width="28" height="28" /></a>
<a href="https://claude.ai" title="Claude" target="_blank" rel="noopener noreferrer"><img src="https://logo.svgcdn.com/logos/claude-icon.png" alt="Claude" width="28" height="28" /></a>
<a href="https://cluely.com" title="Cluely" target="_blank" rel="noopener noreferrer"><img src="https://cluely.com/favicon/light/favicon-256x256.png" alt="Cluely" width="28" height="28" /></a>
<a href="https://cohere.com" title="Cohere Models" target="_blank" rel="noopener noreferrer"><img src="https://registry.npmmirror.com/@lobehub/icons-static-png/latest/files/dark/cohere-color.png" alt="Cohere Models" width="28" height="28" /></a>
<a href="https://contextual.ai" title="Contextual AI Parse" target="_blank" rel="noopener noreferrer"><img src="https://media.licdn.com/dms/image/v2/D560BAQEUQLTc-jpQAQ/company-logo_200_200/B56ZnCdNsrIYAI-/0/1759904062535/contextualai_logo?e=2147483647&amp;v=beta&amp;t=Nfk2U-IagqdIr1crFoT4nS9MkEeA2_BYNGsGiFl6rJM" alt="Contextual AI Parse" width="28" height="28" /></a>
<a href="https://dappier.com" title="Dappier" target="_blank" rel="noopener noreferrer"><img src="https://media.licdn.com/dms/image/v2/D560BAQFdrIzjGOTZOA/company-logo_200_200/company-logo_200_200/0/1703359020005/dappier_logo?e=2147483647&amp;v=beta&amp;t=orrORgQHGHOyKEIntRC08lcAwE8e7uBuUnjHdqXkzSM" alt="Dappier" width="28" height="28" /></a>
<a href="https://app.declaree.com/sso" title="Declaree" target="_blank" rel="noopener noreferrer"><img src="https://appwiki.nl/uploads/media/5c0fbade0955d/declaree-logo.png" alt="Declaree" width="28" height="28" /></a>
<a href="https://developers.deepgram.com" title="Deepgram Audio" target="_blank" rel="noopener noreferrer"><img src="https://avatars.githubusercontent.com/u/17422641?s=280&amp;v=4" alt="Deepgram Audio" width="28" height="28" /></a>
<a href="https://www.deepl.com" title="DeepL" target="_blank" rel="noopener noreferrer"><img src="https://hel1.your-objectstorage.com/ztudium-cms/deepl_7baa54aa02.jpeg" alt="DeepL" width="28" height="28" /></a>
<a href="https://www.deskbird.com" title="deskbird" target="_blank" rel="noopener noreferrer"><img src="https://play-lh.googleusercontent.com/sm-3HAM8_E7a1e0OaaxGAqsi9p5iocX0r3pDmjVXBSWouh0CF95gn09lpvBa8W9MJw" alt="deskbird" width="28" height="28" /></a>
<a href="https://www.duo.nl" title="Dienst Uitvoering Onderwijs (DUO)" target="_blank" rel="noopener noreferrer"><img src="https://media.licdn.com/dms/image/v2/C560BAQHchSWlNHTy4g/company-logo_200_200/company-logo_200_200/0/1642004227429/dienst_uitvoering_onderwijs_ministerie_van_ocw__logo?e=2147483647&amp;v=beta&amp;t=RPf8ycvAPdFfARCxQyuHvzKYo71V09eCGGAXOonmlV8" alt="Dienst Uitvoering Onderwijs (DUO)" width="28" height="28" /></a>
<a href="https://www.edenai.co" title="Eden AI Audio" target="_blank" rel="noopener noreferrer"><img src="https://meta-q.cdn.bubble.io/cdn-cgi/image/w=,h=,f=auto,dpr=1,fit=contain/f1680187367069x814617384157327800/edenlogowebclip.png" alt="Eden AI Audio" width="28" height="28" /></a>
<a href="https://www.ep-online.nl" title="EP Online" target="_blank" rel="noopener noreferrer"><img src="https://upload.wikimedia.org/wikipedia/commons/e/e9/Logo_Rijksoverheid_%28kleur%29.jpg" alt="EP Online" width="28" height="28" /></a>
<a href="https://www.ecb.europa.eu" title="European Central Bank" target="_blank" rel="noopener noreferrer"><img src="https://upload.wikimedia.org/wikipedia/commons/thumb/c/cb/Logo_European_Central_Bank.svg/1200px-Logo_European_Central_Bank.svg.png" alt="European Central Bank" width="28" height="28" /></a>
<a href="https://ec.europa.eu/eurostat" title="European Union Eurostat" target="_blank" rel="noopener noreferrer"><img src="https://upload.wikimedia.org/wikipedia/en/2/27/EU_flag_square.PNG" alt="European Union Eurostat" width="28" height="28" /></a>
<a href="https://www.eurouter.ai" title="EUrouter" target="_blank" rel="noopener noreferrer"><img src="https://www.eurouter.ai/favicon.ico" alt="EUrouter" width="28" height="28" /></a>
<a href="https://exa.ai" title="Exa" target="_blank" rel="noopener noreferrer"><img src="https://media.licdn.com/dms/image/v2/D4D0BAQEGEKPKKLiNvA/company-logo_200_200/company-logo_200_200/0/1721090778302/exa_ai_logo?e=2147483647&amp;v=beta&amp;t=bNJAmBL2v359QkVTUgGTEbdBOqsnYSMaOuCtMDuG920" alt="Exa" width="28" height="28" /></a>
<a href="https://www.firecrawl.dev" title="Firecrawl" target="_blank" rel="noopener noreferrer"><img src="https://raw.githubusercontent.com/firecrawl/firecrawl/main/img/firecrawl_logo.png" alt="Firecrawl" width="28" height="28" /></a>
<a href="https://frankfurter.dev" title="Frankfurter" target="_blank" rel="noopener noreferrer"><img src="https://frankfurter.dev/favicon.png?v=1757050344" alt="Frankfurter" width="28" height="28" /></a>
<a href="https://www.freepik.com" title="Freepik" target="_blank" rel="noopener noreferrer"><img src="https://cdn.freebiesupply.com/logos/large/2x/freepik-logo-png-transparent.png" alt="Freepik" width="28" height="28" /></a>
<a href="https://github.com/AngleSharp/AngleSharp" title="GitHub AngleSharp" target="_blank" rel="noopener noreferrer"><img src="https://raw.githubusercontent.com/AngleSharp/AngleSharp/master/header.png" alt="GitHub AngleSharp" width="28" height="28" /></a>
<a href="https://plotly.net" title="GitHub Plotly" target="_blank" rel="noopener noreferrer"><img src="https://avatars.githubusercontent.com/u/5997976?s=280&amp;v=4" alt="GitHub Plotly" width="28" height="28" /></a>
<a href="https://www.questpdf.com" title="GitHub QuestPDF" target="_blank" rel="noopener noreferrer"><img src="https://avatars.githubusercontent.com/u/73763469?s=200&amp;v=4" alt="GitHub QuestPDF" width="28" height="28" /></a>
<a href="https://www.gladia.io" title="Gladia" target="_blank" rel="noopener noreferrer"><img src="https://pbs.twimg.com/profile_images/1671129329302896640/bX2pGpi0_400x400.jpg" alt="Gladia" width="28" height="28" /></a>
<a href="https://greenpt.ai" title="GreenPT Documents" target="_blank" rel="noopener noreferrer"><img src="https://greenpt.ai/content/uploads/2026/01/2993679_brand_brands_logo_logos_opera_icon@2x-800x800.webp" alt="GreenPT Documents" width="28" height="28" /></a>
<a href="https://groq.com" title="Groq Audio" target="_blank" rel="noopener noreferrer"><img src="https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcTnze6t-thGVKlIKNKF9zeiTfaoxLdYdVzX0g&amp;s" alt="Groq Audio" width="28" height="28" /></a>
<a href="https://imagga.com" title="Imagga" target="_blank" rel="noopener noreferrer"><img src="https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcT4xaoiPW9IzRpTBZ5x_iZy0FEZxGjbHxCqYw&amp;s" alt="Imagga" width="28" height="28" /></a>
<a href="https://jina.ai" title="Jina AI" target="_blank" rel="noopener noreferrer"><img src="https://registry.npmmirror.com/@lobehub/icons-static-png/latest/files/dark/jina.png" alt="Jina AI" width="28" height="28" /></a>
<a href="https://kroki.io" title="Kroki" target="_blank" rel="noopener noreferrer"><img src="https://avatars.githubusercontent.com/u/4894788?v=4" alt="Kroki" width="28" height="28" /></a>
<a href="https://www.linkup.so" title="Linkup" target="_blank" rel="noopener noreferrer"><img src="https://media.licdn.com/dms/image/v2/D4E0BAQEEGNZC-8dPRQ/company-logo_200_200/company-logo_200_200/0/1729583639213/linkup_platform_logo?e=2147483647&amp;v=beta&amp;t=TkWz7Te7mUUAgp8AB6zj4ZM-miaKmeDdu_8FOISgjVU" alt="Linkup" width="28" height="28" /></a>
<a href="https://www.luchtmeetnet.nl" title="Luchtmeetnet" target="_blank" rel="noopener noreferrer"><img src="https://www.luchtmeetnet.nl/_next/static/images/luchtmeetnet.png" alt="Luchtmeetnet" width="28" height="28" /></a>
<a href="https://www.markdownguide.org" title="Markdown" target="_blank" rel="noopener noreferrer"><img src="https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcSgsMgPrbgdUG2CApDYI49SPyiDueMV3rJ9sCdOr4tnxqLimEbIrdyVRrY_uvQXR1-eGfc&amp;usqp=CAU" alt="Markdown" width="28" height="28" /></a>
<a href="https://mem0.ai" title="Mem0 Personal Memory" target="_blank" rel="noopener noreferrer"><img src="https://avatars.githubusercontent.com/u/137054526?v=4" alt="Mem0 Personal Memory" width="28" height="28" /></a>
<a href="https://www.whatsapp.com/meta-ai" title="Meta AI WhatsApp" target="_blank" rel="noopener noreferrer"><img src="https://upload.wikimedia.org/wikipedia/commons/thumb/6/6b/WhatsApp.svg/512px-WhatsApp.svg.png" alt="Meta AI WhatsApp" width="28" height="28" /></a>
<a href="https://www.office.com" title="Microsoft 365 Me" target="_blank" rel="noopener noreferrer"><img src="https://upload.wikimedia.org/wikipedia/commons/thumb/0/0e/Microsoft_365_%282022%29.svg/1862px-Microsoft_365_%282022%29.svg.png" alt="Microsoft 365 Me" width="28" height="28" /></a>
<a href="https://outlook.office.com/bookings" title="Microsoft Bookings" target="_blank" rel="noopener noreferrer"><img src="https://store-images.s-microsoft.com/image/apps.53739.9d34a54c-e776-490b-aa19-4ed98464d29e.3b1aa51c-88f6-49da-b578-51f701696daa.9885235c-592c-4112-84b1-0000b25c6588.png" alt="Microsoft Bookings" width="28" height="28" /></a>
<a href="https://m365.cloud.microsoft/launch/onenote" title="Microsoft OneNote" target="_blank" rel="noopener noreferrer"><img src="https://upload.wikimedia.org/wikipedia/commons/thumb/3/34/Microsoft_OneNote_Icon_%282025_-_present%29.svg/1166px-Microsoft_OneNote_Icon_%282025_-_present%29.svg.png" alt="Microsoft OneNote" width="28" height="28" /></a>
<a href="https://planner.cloud.microsoft" title="Microsoft Planner" target="_blank" rel="noopener noreferrer"><img src="https://upload.wikimedia.org/wikipedia/commons/thumb/d/d4/Microsoft_Planner_%282024%E2%80%93present%29.svg/1200px-Microsoft_Planner_%282024%E2%80%93present%29.svg.png" alt="Microsoft Planner" width="28" height="28" /></a>
<a href="https://teams.microsoft.com" title="Microsoft Teams" target="_blank" rel="noopener noreferrer"><img src="https://www.computerhope.com/jargon/m/microsoft-teams.png" alt="Microsoft Teams" width="28" height="28" /></a>
<a href="https://excel.cloud.microsoft" title="Microsoft Workbooks" target="_blank" rel="noopener noreferrer"><img src="https://upload.wikimedia.org/wikipedia/commons/thumb/6/60/Microsoft_Office_Excel_%282025%E2%80%93present%29.svg/1166px-Microsoft_Office_Excel_%282025%E2%80%93present%29.svg.png" alt="Microsoft Workbooks" width="28" height="28" /></a>
<a href="https://www.rijksfinancien.nl" title="Ministerie van Financi&#195;&#171;n" target="_blank" rel="noopener noreferrer"><img src="https://media.licdn.com/dms/image/v2/D4E0BAQF6QwdZ2sseQg/company-logo_200_200/company-logo_200_200/0/1693986937829/ministry_of_finance_of_netherlands_logo?e=2147483647&amp;v=beta&amp;t=DTae_hRUvXA4om4QcGNhOFJsX03AqzOf6LqhnaSNFUY" alt="Ministerie van Financi&#195;&#171;n" width="28" height="28" /></a>
<a href="https://mistral.ai" title="Mistral Agents" target="_blank" rel="noopener noreferrer"><img src="https://upload.wikimedia.org/wikipedia/commons/thumb/e/e6/Mistral_AI_logo_%282025%E2%80%93%29.svg/1200px-Mistral_AI_logo_%282025%E2%80%93%29.svg.png" alt="Mistral Agents" width="28" height="28" /></a>
<a href="https://docs.mistral.ai/capabilities/document_ai/document_ai_overview" title="Mistral DocumentAI" target="_blank" rel="noopener noreferrer"><img src="https://upload.wikimedia.org/wikipedia/commons/thumb/e/e6/Mistral_AI_logo_%282025%E2%80%93%29.svg/1200px-Mistral_AI_logo_%282025%E2%80%93%29.svg.png" alt="Mistral DocumentAI" width="28" height="28" /></a>
<a href="https://modelcontextprotocol.io" title="ModelContext Documentation" target="_blank" rel="noopener noreferrer"><img src="https://registry.npmmirror.com/@lobehub/icons-static-png/latest/files/dark/mcp.png" alt="ModelContext Documentation" width="28" height="28" /></a>
<a href="https://date.nager.at" title="Nager.Date" target="_blank" rel="noopener noreferrer"><img src="https://avatars.githubusercontent.com/u/60408767?v=4" alt="Nager.Date" width="28" height="28" /></a>
<a href="https://nederlandwereldwijd.nl" title="Nederland Wereldwijd" target="_blank" rel="noopener noreferrer"><img src="https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcT2iQhd7hj99WHMJaUioSjAoW3uzvJ9N-CFaQ&amp;s" alt="Nederland Wereldwijd" width="28" height="28" /></a>
<a href="https://www.nrvt.nl" title="Nederlands Register Vastgoed Taxateurs" target="_blank" rel="noopener noreferrer"><img src="https://www.urbanproperties.nl/storage/image/2024/07/17/183/1200x900_nrvt-logo-afbeelding.jpg" alt="Nederlands Register Vastgoed Taxateurs" width="28" height="28" /></a>
<a href="https://www.nvm.nl" title="Nederlandse Vereniging van Makelaars" target="_blank" rel="noopener noreferrer"><img src="https://roz.nl/wp-content/uploads/2016/11/nvm-logo.png" alt="Nederlandse Vereniging van Makelaars" width="28" height="28" /></a>
<a href="https://nominatim.org" title="Nominatim" target="_blank" rel="noopener noreferrer"><img src="https://nlnet.nl/project/Nominatim-lib/openstreetmap.logo.svg" alt="Nominatim" width="28" height="28" /></a>
<a href="https://openspending.nl" title="Open Spending" target="_blank" rel="noopener noreferrer"><img src="https://openstate.eu/wp-content/uploads/sites/14/2025/04/open-spending-logo-480x375.png" alt="Open Spending" width="28" height="28" /></a>
<a href="https://opentdb.com" title="Open Trivia" target="_blank" rel="noopener noreferrer"><img src="https://opentdb.com/images/logo.png" alt="Open Trivia" width="28" height="28" /></a>
<a href="https://chatgpt.com/studymode" title="OpenAI ChatGPT Study" target="_blank" rel="noopener noreferrer"><img src="https://registry.npmmirror.com/@lobehub/icons-static-png/latest/files/dark/openai.png" alt="OpenAI ChatGPT Study" width="28" height="28" /></a>
<a href="https://www.openai.com" title="OpenAI Files" target="_blank" rel="noopener noreferrer"><img src="https://registry.npmmirror.com/@lobehub/icons-static-png/latest/files/dark/openai.png" alt="OpenAI Files" width="28" height="28" /></a>
<a href="https://www.opengraph.io" title="OpenGraph.io" target="_blank" rel="noopener noreferrer"><img src="https://rapidapi.com/hub/_next/image?url=https%3A%2F%2Frapidapi-prod-apis.s3.amazonaws.com%2Fea38ea47-cc4a-4f01-a317-16e9a9f6daaf.png&amp;w=3840&amp;q=75" alt="OpenGraph.io" width="28" height="28" /></a>
<a href="https://open-meteo.com" title="Open-Meteo" target="_blank" rel="noopener noreferrer"><img src="https://avatars.githubusercontent.com/u/86407831?v=4" alt="Open-Meteo" width="28" height="28" /></a>
<a href="https://www.openplzapi.org" title="OpenPLZ" target="_blank" rel="noopener noreferrer"><img src="https://avatars.githubusercontent.com/u/108539274?v=4" alt="OpenPLZ" width="28" height="28" /></a>
<a href="https://opper.ai" title="Opper AI Models" target="_blank" rel="noopener noreferrer"><img src="https://avatars.githubusercontent.com/u/145928785?s=280&amp;v=4" alt="Opper AI Models" width="28" height="28" /></a>
<a href="https://parallel.ai" title="Parallel" target="_blank" rel="noopener noreferrer"><img src="https://media.licdn.com/dms/image/v2/D560BAQGXvJBt9J3NKg/company-logo_200_200/B56ZbvGWMYHUAQ-/0/1747768140820/parallel_web_logo?e=2147483647&amp;v=beta&amp;t=NS4V8dGByW1NDw80Vs4g6U3XxmAwXHosbWOi9jlaz2o" alt="Parallel" width="28" height="28" /></a>
<a href="https://www.perplexity.ai" title="Perplexity" target="_blank" rel="noopener noreferrer"><img src="https://brandlogos.net/wp-content/uploads/2025/05/perplexity_icon-logo_brandlogos.net_a9d3e-512x591.png" alt="Perplexity" width="28" height="28" /></a>
<a href="https://www.pexels.com" title="Pexels" target="_blank" rel="noopener noreferrer"><img src="https://img.utdstc.com/icon/101/937/101937caaf5d051d0a72eae4d98f2abb9ef51e6f4fcfb43a74006d54590c79cd:200" alt="Pexels" width="28" height="28" /></a>
<a href="https://pollinations.ai" title="Pollinations" target="_blank" rel="noopener noreferrer"><img src="https://avatars.githubusercontent.com/u/86964862?s=200&amp;v=4" alt="Pollinations" width="28" height="28" /></a>
<a href="https://portkey.ai" title="Portkey" target="_blank" rel="noopener noreferrer"><img src="https://media.licdn.com/dms/image/v2/D4D0BAQF6D3Bf64c7_Q/company-logo_200_200/company-logo_200_200/0/1706756517195/portkey_ai_logo?e=2147483647&amp;v=beta&amp;t=m4yu6L4zWSJ_N1EPpuWrqN3BN7sDZzprqPqQqnpllCE" alt="Portkey" width="28" height="28" /></a>
<a href="https://www.microsoft.com/en-us/power-platform/products/power-automate" title="Power Automate" target="_blank" rel="noopener noreferrer"><img src="https://upload.wikimedia.org/wikipedia/commons/thumb/4/4d/Microsoft_Power_Automate.svg/2048px-Microsoft_Power_Automate.svg.png" alt="Power Automate" width="28" height="28" /></a>
<a href="https://app.powerbi.com" title="Power BI" target="_blank" rel="noopener noreferrer"><img src="https://upload.wikimedia.org/wikipedia/commons/thumb/c/cf/New_Power_BI_Logo.svg/1200px-New_Power_BI_Logo.svg.png" alt="Power BI" width="28" height="28" /></a>
<a href="https://www.gutenberg.org" title="Project Gutenberg" target="_blank" rel="noopener noreferrer"><img src="https://upload.wikimedia.org/wikipedia/commons/thumb/5/59/Project_Gutenberg_logo.svg/2048px-Project_Gutenberg_logo.svg.png" alt="Project Gutenberg" width="28" height="28" /></a>
<a href="https://www.pdok.nl" title="Publieke Dienstverlening op de Kaart" target="_blank" rel="noopener noreferrer"><img src="https://avatars.githubusercontent.com/u/7768379?s=280&amp;v=4" alt="Publieke Dienstverlening op de Kaart" width="28" height="28" /></a>
<a href="https://www.recraft.ai" title="Recraft" target="_blank" rel="noopener noreferrer"><img src="https://registry.npmmirror.com/@lobehub/icons-static-png/latest/files/dark/recraft.png" alt="Recraft" width="28" height="28" /></a>
<a href="https://regolo.ai" title="RegoloAI Images" target="_blank" rel="noopener noreferrer"><img src="https://regolo.ai/wp-content/themes/regolo/img/hero-image.png" alt="RegoloAI Images" width="28" height="28" /></a>
<a href="https://reka.ai" title="RekaAI Models" target="_blank" rel="noopener noreferrer"><img src="https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcR8CsOEbRgvL2rm3WdVU2bSTfZp2Vpli4tKTg&amp;s" alt="RekaAI Models" width="28" height="28" /></a>
<a href="https://replicate.com" title="Replicate" target="_blank" rel="noopener noreferrer"><img src="https://images.seeklogo.com/logo-png/61/1/replicate-icon-logo-png_seeklogo-611690.png" alt="Replicate" width="28" height="28" /></a>
<a href="https://www.rdw.nl" title="Rijksdienst Wegverkeer (RDW)" target="_blank" rel="noopener noreferrer"><img src="https://media.licdn.com/dms/image/v2/C560BAQHoEMpSiMaeNA/company-logo_200_200/company-logo_200_200/0/1630630477515/rdw_logo?e=2147483647&amp;v=beta&amp;t=7cFzcIGmi_m3Bf1t2PBwHbQBOI4QTgfAWgNuWENqBVs" alt="Rijksdienst Wegverkeer (RDW)" width="28" height="28" /></a>
<a href="https://www.rijksmuseum.nl" title="Rijksmuseum" target="_blank" rel="noopener noreferrer"><img src="https://itemsmagazine.com/media/uploads/images/Rijksmuseum_aug2012_logo_HR.jpg" alt="Rijksmuseum" width="28" height="28" /></a>
<a href="https://www.rijksoverheid.nl" title="Rijksoverheid" target="_blank" rel="noopener noreferrer"><img src="https://upload.wikimedia.org/wikipedia/commons/e/e9/Logo_Rijksoverheid_%28kleur%29.jpg" alt="Rijksoverheid" width="28" height="28" /></a>
<a href="https://runware.ai" title="Runware Audio" target="_blank" rel="noopener noreferrer"><img src="https://media.licdn.com/dms/image/v2/D4E0BAQFzsJTSfpkCbQ/company-logo_200_200/B4EZfR3QUwGcAQ-/0/1751572612962/runware_logo?e=2147483647&amp;v=beta&amp;t=HvfRm7Kk85KGMPVDzoiTBWuFN9v5bnyFSRvPuie2BBQ" alt="Runware Audio" width="28" height="28" /></a>
<a href="https://runwayml.com" title="Runway Audio" target="_blank" rel="noopener noreferrer"><img src="https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcQQE01cLcJx7cJONWLVmk5tRBhGB0LIJ8SqSQ&amp;s" alt="Runway Audio" width="28" height="28" /></a>
<a href="https://www.scaleway.com" title="Scaleway Audio" target="_blank" rel="noopener noreferrer"><img src="https://www-uploads.scaleway.com/Scaleway_3_D_Logo_57e7fb833f.png" alt="Scaleway Audio" width="28" height="28" /></a>
<a href="https://scrapegraphai.com" title="ScrapeGraphAI" target="_blank" rel="noopener noreferrer"><img src="https://avatars.githubusercontent.com/u/171017415?s=280&amp;v=4" alt="ScrapeGraphAI" width="28" height="28" /></a>
<a href="https://www.siliconflow.com" title="SiliconFlow Audio" target="_blank" rel="noopener noreferrer"><img src="https://raw.githubusercontent.com/lobehub/lobe-icons/refs/heads/master/packages/static-png/dark/siliconcloud-color.png" alt="SiliconFlow Audio" width="28" height="28" /></a>
<a href="https://smithery.ai" title="Smithery" target="_blank" rel="noopener noreferrer"><img src="https://registry.npmmirror.com/@lobehub/icons-static-png/latest/files/dark/smithery-color.png" alt="Smithery" width="28" height="28" /></a>
<a href="https://stability.ai" title="StabilityAI 3D" target="_blank" rel="noopener noreferrer"><img src="https://media.licdn.com/dms/image/v2/D560BAQGCth_DU2z1Fg/company-logo_200_200/B56Zh18sPOHMAU-/0/1754325501246/stability_ai_logo?e=2147483647&amp;v=beta&amp;t=K2vnKQAZRmg2Nqe0fJY7sYHDbLA752NOG1E45JbZC5s" alt="StabilityAI 3D" width="28" height="28" /></a>
<a href="https://eklok.nl" title="Stedin eKlok" target="_blank" rel="noopener noreferrer"><img src="https://eklok.nl/images/klokkie.png" alt="Stedin eKlok" width="28" height="28" /></a>
<a href="https://sudoapp.dev" title="Sudo" target="_blank" rel="noopener noreferrer"><img src="https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcQHpkT8SvBa8pZvJeKuxvxVuSpcFAXnwHz-Hg&amp;s" alt="Sudo" width="28" height="28" /></a>
<a href="https://sunoapi.org" title="SunoAPI Music" target="_blank" rel="noopener noreferrer"><img src="https://sunoapi.org/logo.png" alt="SunoAPI Music" width="28" height="28" /></a>
<a href="https://sunrise-sunset.org" title="Sunrise Sunset" target="_blank" rel="noopener noreferrer"><img src="https://c1.tablecdn.com/pa/sunrisesunset.png" alt="Sunrise Sunset" width="28" height="28" /></a>
<a href="https://www.tavily.com" title="Tavily" target="_blank" rel="noopener noreferrer"><img src="https://registry.npmmirror.com/@lobehub/icons-static-png/latest/files/dark/tavily-color.png" alt="Tavily" width="28" height="28" /></a>
<a href="https://www.tavus.io" title="Tavus" target="_blank" rel="noopener noreferrer"><img src="https://media.licdn.com/dms/image/v2/D560BAQGVTHSGytjN4Q/company-logo_200_200/B56ZlTXdvZHAAM-/0/1758040284462/tavus_io_logo?e=2147483647&amp;v=beta&amp;t=T3Jwj6QSOqDBepOHWpE8y6w005Q2GyWwNlHpW3mhWg4" alt="Tavus" width="28" height="28" /></a>
<a href="https://telnyx.com" title="Telnyx" target="_blank" rel="noopener noreferrer"><img src="https://media.glassdoor.com/sqll/841349/telnyx-squareLogo-1692104355572.png" alt="Telnyx" width="28" height="28" /></a>
<a href="https://www.tenderned.nl" title="TenderNed" target="_blank" rel="noopener noreferrer"><img src="https://www.detirrel.nl/wp-content/uploads/2019/01/logo-tenderned.png" alt="TenderNed" width="28" height="28" /></a>
<a href="https://tinfoil.sh" title="Tinfoil" target="_blank" rel="noopener noreferrer"><img src="https://tinfoil.sh/favicon.ico" alt="Tinfoil" width="28" height="28" /></a>
<a href="https://todo.microsoft.com" title="Todo Task List" target="_blank" rel="noopener noreferrer"><img src="https://upload.wikimedia.org/wikipedia/commons/6/67/Microsoft_To-Do_icon.png" alt="Todo Task List" width="28" height="28" /></a>
<a href="https://www.together.ai" title="Together Audio" target="_blank" rel="noopener noreferrer"><img src="https://registry.npmmirror.com/@lobehub/icons-static-png/latest/files/light/together-color.png" alt="Together Audio" width="28" height="28" /></a>
<a href="https://gegevensmagazijn.tweedekamer.nl" title="Tweede Kamer der Staten-Generaal" target="_blank" rel="noopener noreferrer"><img src="https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcTNFweh5EWE1Szut4pCdHfkLVYLTAcT7mFRqw&amp;s" alt="Tweede Kamer der Staten-Generaal" width="28" height="28" /></a>
<a href="https://unsplash.com" title="Unsplash" target="_blank" rel="noopener noreferrer"><img src="https://cdn-icons-png.flaticon.com/256/5968/5968763.png" alt="Unsplash" width="28" height="28" /></a>
<a href="https://www.upstage.ai" title="Upstage Document Classification" target="_blank" rel="noopener noreferrer"><img src="https://raw.githubusercontent.com/lobehub/lobe-icons/refs/heads/master/packages/static-png/dark/upstage-color.png" alt="Upstage Document Classification" width="28" height="28" /></a>
<a href="https://www.vektis.nl" title="Vektis" target="_blank" rel="noopener noreferrer"><img src="https://media.glassdoor.com/sqll/4762824/vektis-squarelogo-1644495778896.png" alt="Vektis" width="28" height="28" /></a>
<a href="https://www.vidu.com" title="Vidu" target="_blank" rel="noopener noreferrer"><img src="https://raw.githubusercontent.com/lobehub/lobe-icons/refs/heads/master/packages/static-png/dark/vidu-color.png" alt="Vidu" width="28" height="28" /></a>
<a href="https://www.voyageai.com" title="Voyage AI Reranker" target="_blank" rel="noopener noreferrer"><img src="https://blog.voyageai.com/wp-content/uploads/2023/10/logo.png" alt="Voyage AI Reranker" width="28" height="28" /></a>
<a href="https://rijkswaterstaatdata.nl/projecten/beta-waterwebservices/" title="WaterData Rijkswaterstaat (WADAR)" target="_blank" rel="noopener noreferrer"><img src="https://upload.wikimedia.org/wikipedia/commons/e/e9/Logo_Rijksoverheid_%28kleur%29.jpg" alt="WaterData Rijkswaterstaat (WADAR)" width="28" height="28" /></a>
<a href="https://www.wikidata.org" title="Wikidata" target="_blank" rel="noopener noreferrer"><img src="https://upload.wikimedia.org/wikipedia/commons/8/8b/Wikidata-logo-en_with_square_background_%28needed_for_interfaces%29.svg" alt="Wikidata" width="28" height="28" /></a>
<a href="https://aqicn.org" title="World Air Quality Index" target="_blank" rel="noopener noreferrer"><img src="https://aqicn.org/air/images/aqicnxl.png" alt="World Air Quality Index" width="28" height="28" /></a>
<a href="https://www.wtcrotterdam.com" title="WTC Rotterdam" target="_blank" rel="noopener noreferrer"><img src="https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcTUSNvML84vSq0RE4rSAVfkI8qlugXr-KBzEg&amp;s" alt="WTC Rotterdam" width="28" height="28" /></a>
<a href="https://x.ai" title="xAI Code Execution" target="_blank" rel="noopener noreferrer"><img src="https://registry.npmmirror.com/@lobehub/icons-static-png/1.74.0/files/dark/xai.png" alt="xAI Code Execution" width="28" height="28" /></a>
<a href="https://xeno-canto.org" title="xeno-canto" target="_blank" rel="noopener noreferrer"><img src="https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcThemRNaqdOnyGTRpvyjKGukVfDoQ3GGHcmiA&amp;s" alt="xeno-canto" width="28" height="28" /></a>
<a href="https://docs.z.ai" title="z.AI Tools" target="_blank" rel="noopener noreferrer"><img src="https://avatars.githubusercontent.com/u/223098841?s=200&amp;v=4" alt="z.AI Tools" width="28" height="28" /></a>
<a href="https://www.zorginzicht.nl" title="Zorginzicht" target="_blank" rel="noopener noreferrer"><img src="https://www.zorginzicht.nl/assets/favicon/touch-icon.png" alt="Zorginzicht" width="28" height="28" /></a>
<a href="https://www.zorgkaartnederland.nl" title="Zorgkaart Nederland" target="_blank" rel="noopener noreferrer"><img src="https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcRPHNjn3ZPSr6c8IxSWGqN86zVwdk2wKyCrYg&amp;s" alt="Zorgkaart Nederland" width="28" height="28" /></a>
</p>

<details>
<summary><strong>Accessibility fallback (alphabetical provider links)</strong></summary>

[302.AI 3D Modelling](https://302.ai) | [AI/ML Audio](https://aimlapi.com) | [Anthropic Code Execution](https://www.anthropic.com) | [AssemblyAI](https://www.assemblyai.com) | [asyncAI](https://async.ai) | [Audixa](https://audixa.ai) | [Azure Document Intelligence](https://azure.microsoft.com/en-us/products/ai-services/ai-document-intelligence) | [Azure Service Management](https://portal.azure.com) | [Basisregistratie Adressen en Gebouwen (BAG)](https://www.kadaster.nl/zakelijk/registraties/basisregistraties/bag) | [Berget AI Models](https://berget.ai) | [Brave](https://brave.com) | [Bria](https://bria.ai) | [BrightData](https://brightdata.com/) | [Calcasa](https://calcasa.nl) | [Capital Value](https://www.capitalvalue.nl) | [Centraal Bureau voor de Statistiek (CBS)](https://www.cbs.nl) | [Centrum Onderzoek Economie Lagere Overheden](https://coelo.nl) | [Claude](https://claude.ai) | [Cluely](https://cluely.com) | [Cohere Models](https://cohere.com) | [Contextual AI Parse](https://contextual.ai) | [Dappier](https://dappier.com) | [Declaree](https://app.declaree.com/sso) | [Deepgram Audio](https://developers.deepgram.com) | [DeepL](https://www.deepl.com) | [deskbird](https://www.deskbird.com) | [Dienst Uitvoering Onderwijs (DUO)](https://www.duo.nl) | [Eden AI Audio](https://www.edenai.co) | [EP Online](https://www.ep-online.nl) | [European Central Bank](https://www.ecb.europa.eu) | [European Union Eurostat](https://ec.europa.eu/eurostat) | [EUrouter](https://www.eurouter.ai) | [Exa](https://exa.ai) | [Firecrawl](https://www.firecrawl.dev) | [Frankfurter](https://frankfurter.dev) | [Freepik](https://www.freepik.com) | [GitHub AngleSharp](https://github.com/AngleSharp/AngleSharp) | [GitHub Plotly](https://plotly.net) | [GitHub QuestPDF](https://www.questpdf.com) | [Gladia](https://www.gladia.io) | [GreenPT Documents](https://greenpt.ai) | [Groq Audio](https://groq.com) | [Imagga](https://imagga.com) | [Jina AI](https://jina.ai) | [Kroki](https://kroki.io) | [Linkup](https://www.linkup.so) | [Luchtmeetnet](https://www.luchtmeetnet.nl) | [Markdown](https://www.markdownguide.org) | [Mem0 Personal Memory](https://mem0.ai) | [Meta AI WhatsApp](https://www.whatsapp.com/meta-ai) | [Microsoft 365 Me](https://www.office.com) | [Microsoft Bookings](https://outlook.office.com/bookings) | [Microsoft OneNote](https://m365.cloud.microsoft/launch/onenote) | [Microsoft Planner](https://planner.cloud.microsoft) | [Microsoft Teams](https://teams.microsoft.com) | [Microsoft Workbooks](https://excel.cloud.microsoft) | [Ministerie van Financi&#195;&#171;n](https://www.rijksfinancien.nl) | [Mistral Agents](https://mistral.ai) | [Mistral DocumentAI](https://docs.mistral.ai/capabilities/document_ai/document_ai_overview) | [ModelContext Documentation](https://modelcontextprotocol.io) | [Nager.Date](https://date.nager.at) | [Nederland Wereldwijd](https://nederlandwereldwijd.nl) | [Nederlands Register Vastgoed Taxateurs](https://www.nrvt.nl) | [Nederlandse Vereniging van Makelaars](https://www.nvm.nl) | [Nominatim](https://nominatim.org) | [Open Spending](https://openspending.nl) | [Open Trivia](https://opentdb.com) | [OpenAI ChatGPT Study](https://chatgpt.com/studymode) | [OpenAI Files](https://www.openai.com) | [OpenGraph.io](https://www.opengraph.io) | [Open-Meteo](https://open-meteo.com) | [OpenPLZ](https://www.openplzapi.org) | [Opper AI Models](https://opper.ai) | [Parallel](https://parallel.ai) | [Perplexity](https://www.perplexity.ai) | [Pexels](https://www.pexels.com) | [Pollinations](https://pollinations.ai) | [Portkey](https://portkey.ai) | [Power Automate](https://www.microsoft.com/en-us/power-platform/products/power-automate) | [Power BI](https://app.powerbi.com) | [Project Gutenberg](https://www.gutenberg.org) | [Publieke Dienstverlening op de Kaart](https://www.pdok.nl) | [Recraft](https://www.recraft.ai) | [RegoloAI Images](https://regolo.ai) | [RekaAI Models](https://reka.ai) | [Replicate](https://replicate.com) | [Rijksdienst Wegverkeer (RDW)](https://www.rdw.nl) | [Rijksmuseum](https://www.rijksmuseum.nl) | [Rijksoverheid](https://www.rijksoverheid.nl) | [Runware Audio](https://runware.ai) | [Runway Audio](https://runwayml.com) | [Scaleway Audio](https://www.scaleway.com) | [ScrapeGraphAI](https://scrapegraphai.com) | [SiliconFlow Audio](https://www.siliconflow.com) | [Smithery](https://smithery.ai) | [StabilityAI 3D](https://stability.ai) | [Stedin eKlok](https://eklok.nl) | [Sudo](https://sudoapp.dev) | [SunoAPI Music](https://sunoapi.org) | [Sunrise Sunset](https://sunrise-sunset.org) | [Tavily](https://www.tavily.com) | [Tavus](https://www.tavus.io) | [Telnyx](https://telnyx.com) | [TenderNed](https://www.tenderned.nl) | [Tinfoil](https://tinfoil.sh) | [Todo Task List](https://todo.microsoft.com) | [Together Audio](https://www.together.ai) | [Tweede Kamer der Staten-Generaal](https://gegevensmagazijn.tweedekamer.nl) | [Unsplash](https://unsplash.com) | [Upstage Document Classification](https://www.upstage.ai) | [Vektis](https://www.vektis.nl) | [Vidu](https://www.vidu.com) | [Voyage AI Reranker](https://www.voyageai.com) | [WaterData Rijkswaterstaat (WADAR)](https://rijkswaterstaatdata.nl/projecten/beta-waterwebservices/) | [Wikidata](https://www.wikidata.org) | [World Air Quality Index](https://aqicn.org) | [WTC Rotterdam](https://www.wtcrotterdam.com) | [xAI Code Execution](https://x.ai) | [xeno-canto](https://xeno-canto.org) | [z.AI Tools](https://docs.z.ai) | [Zorginzicht](https://www.zorginzicht.nl) | [Zorgkaart Nederland](https://www.zorgkaartnederland.nl)

</details>
<!-- PROVIDER_LOGO_GRID_END -->

## Connect to the MCP backend

Default hosted endpoint:

- `https://mcp.aihappey.net`

Typical connection flow:

1. Point your MCP client to the base endpoint.
2. Discover available servers from the registry endpoint.
3. Select the servers relevant to your use case.
4. Authenticate based on your deployment profile (header auth or Azure auth).

Example registry discovery URL:

- `GET https://mcp.aihappey.net/v0.1/servers`

## Repository structure

- [`Abstractions`](Abstractions): authentication, tools, decoders, telemetry, scrapers
- [`Core`](Core): shared MCP hosting/services and core runtime logic
- [`Servers/MCPhappey.Servers.JSON`](Servers/MCPhappey.Servers.JSON): static JSON-defined MCP servers
- [`Servers/MCPhappey.Servers.SQL`](Servers/MCPhappey.Servers.SQL): SQL-backed dynamic MCP servers
- [`Samples/MCPhappey.HeaderAuth`](Samples/MCPhappey.HeaderAuth): sample host with header-based auth
- [`Samples/MCPhappey.AzureAuth`](Samples/MCPhappey.AzureAuth): sample host with Azure auth

## Run locally

Prerequisite:

- **.NET 9 SDK**

Run HeaderAuth sample:

```bash
dotnet run --project Samples/MCPhappey.HeaderAuth/MCPhappey.HeaderAuth.csproj
```

Run AzureAuth sample:

```bash
dotnet run --project Samples/MCPhappey.AzureAuth/MCPhappey.AzureAuth.csproj
```

## Maintenance

Regenerate this README (including the logo wall and counts) with:

```powershell
powershell -ExecutionPolicy Bypass -File ./scripts/generate-readme.ps1
```