# Academic & Comprehensive Research Utilities

This document logs architectural ideas and implementation specifications for academic research, reference mapping, and study tools in Horizon Browser.

---

## 1. Citations & Source Management

### 1.1 Instant Citation Builder
- **Description**: A tool that extracts academic and article metadata from the active tab and generates bibliographies instantly in standard academic formats.
- **Supported Formats**: APA 7th Edition, MLA 9th Edition, Chicago 17th Edition (Author-Date), Harvard, BibTeX.
- **Metadata Sources Extracted**:
  - OpenGraph tags: `og:title`, `og:site_name`, `og:url`
  - Dublin Core: `dc.creator`, `dc.title`, `dc.date`
  - Highwire Press & Google Scholar meta: `citation_title`, `citation_author`, `citation_publication_date`, `citation_journal_title`, `citation_doi`, `citation_volume`, `citation_issue`
  - JSON-LD Schema: `ScholarlyArticle`, `NewsArticle`, `WebPage`
  - Active page fallback: `document.title`, `window.location.href`, accessed date
- **UI & UX**: Modal with format dropdown, formatted citation output box, 1-click copy, and `.bib` / text export.

### 1.2 DOI / Paywall Resolver
- **Description**: Reads an academic paper's Digital Object Identifier (DOI) and locates legal open-access versions, pre-prints, or institutional repositories.
- **Data Providers**: Unpaywall API (`api.unpaywall.org`), Crossref API (`api.crossref.org`), OpenAlex, arXiv, PubMed Central (PMC), and Sci-Hub fallback mirrors.
- **Workflow**:
  1. Auto-extracts DOI pattern `10.\d{4,9}/[-._;()/:A-Za-z0-9]+` from URL or page text.
  2. Queries Unpaywall / Crossref for `best_oa_location.url_for_pdf`.
  3. Displays paper title, journal, open-access status (Gold, Green, Bronze, Closed), and a 1-click "Open Full-Text PDF" button.

### 1.3 ISBN Tracker & Price Evaluator
- **Description**: Pulls International Standard Book Numbers (ISBN-10 / ISBN-13) from textbook listings, syllabus pages, or Amazon listings and checks global library catalogs and second-hand vendors.
- **Integrations**: OpenLibrary API, WorldCat, Google Books API, BookFinder.
- **Output**: Author, publisher, editions, available library borrows, and price comparisons across vendors.

### 1.4 Abstract Summarizer Panel
- **Description**: Extracts the introduction, methodology, results, and conclusion from lengthy academic papers to generate an executive 200-word digest.
- **Processing**: Client-side rule-based extractive text summarizer or local NLP model parsing paper headings (`# Introduction`, `# Abstract`, `# Methods`, `# Conclusion`).

---

## 2. Note-Taking & Reference Mapping

### 2.1 Knowledge Graph Matrix
- **Description**: A visual interactive node-graph connecting all open research tabs based on overlapping topics, keywords, domain entities, and citation cross-references.
- **Rendering**: Interactive canvas using force-directed layout. Nodes represent tabs; edges represent common keywords, shared authors, or domain clusters.

### 2.2 Inline PDF Annotator Layer
- **Description**: Unlocks typing, highlighting, sticky notes, and freehand drawing toolsets on browser-rendered PDF documents without requiring Adobe Acrobat.
- **Features**: Highlight text, add margin comments, export annotated PDF, sync annotations to Horizon Notes.

### 2.3 Plagiarism Checker Engine
- **Description**: Local text scanner that verifies highlighted text or drafted essays against web search indexes to check quotation accuracy and source originality.
- **Algorithm**: N-gram fingerprinting and web query sampling to detect direct text matches and generate a similarity percentage score.

### 2.4 Text Mining & Keyword Density Plotter
- **Description**: Analyzes active webpage text to extract key academic jargon, term frequency-inverse document frequency (TF-IDF), reading grade level, and unique vocabulary metrics.
- **Metrics Calculated**:
  - Word count & character count
  - Unique vocabulary count & lexical diversity index
  - Automated Readability Index (ARI) & Flesch-Kincaid Reading Grade
  - Top 15 recurring keyword frequencies with interactive visual density bars
