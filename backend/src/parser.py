import os
import re
from typing import List, Tuple
import fitz  # PyMuPDF

class RecursiveCharacterSplitter:
    """
    Split text recursively by a hierarchy of delimiters: paragraphs, sentences, words, characters.
    Maintains a target chunk size and overlap window using character counts as an efficient proxy for tokens.
    (Approx: 4 characters per token -> 400 tokens ≈ 1600 characters, 50 tokens ≈ 200 characters overlap)
    """
    def __init__(self, chunk_size: int = 1600, chunk_overlap: int = 200):
        self.chunk_size = chunk_size
        self.chunk_overlap = chunk_overlap
        self.separators = ["\n\n", "\n", ". ", "? ", "! ", " ", ""]

    def split_text(self, text: str) -> List[str]:
        """
        Splits the text recursively.
        """
        return self._split(text, self.separators)

    def _split(self, text: str, separators: List[str]) -> List[str]:
        # If the text is small enough, return it as a single chunk
        if len(text) <= self.chunk_size:
            return [text]

        # Find the first separator that actually splits the text
        separator = ""
        next_separators = []
        for i, sep in enumerate(separators):
            if sep == "":
                separator = sep
                next_separators = separators[i+1:]
                break
            if sep in text:
                separator = sep
                next_separators = separators[i+1:]
                break

        # Split the text
        if separator != "":
            splits = text.split(separator)
        else:
            # Force split if no separators work
            splits = list(text)

        # Merge splits into chunks of self.chunk_size with self.chunk_overlap overlap
        chunks = []
        current_doc = []
        current_len = 0

        for split in splits:
            split_len = len(split)
            
            # If a single split exceeds the chunk size, we must split it recursively
            if split_len > self.chunk_size:
                if current_doc:
                    chunks.append(separator.join(current_doc))
                    current_doc = []
                    current_len = 0
                
                # Split this big chunk recursively with the next delimiters
                sub_chunks = self._split(split, next_separators)
                chunks.extend(sub_chunks)
                continue

            # If adding this split exceeds our limit, save current chunk and handle overlap
            if current_len + split_len + (len(separator) if current_doc else 0) > self.chunk_size:
                if current_doc:
                    chunks.append(separator.join(current_doc))
                
                # Keep sliding window overlap
                # Backtrack elements from current_doc to form the overlap
                overlap_doc = []
                overlap_len = 0
                for doc_part in reversed(current_doc):
                    part_len = len(doc_part)
                    if overlap_len + part_len + (len(separator) if overlap_doc else 0) <= self.chunk_overlap:
                        overlap_doc.insert(0, doc_part)
                        overlap_len += part_len + len(separator)
                    else:
                        break
                
                current_doc = overlap_doc
                current_len = sum(len(x) for x in current_doc) + (len(separator) * (len(current_doc) - 1) if current_doc else 0)

            current_doc.append(split)
            current_len += split_len + (len(separator) if len(current_doc) > 1 else 0)

        if current_doc:
            chunks.append(separator.join(current_doc))

        # Filter out empty or whitespace-only chunks
        return [c.strip() for c in chunks if c.strip()]


class FileParser:
    """
    Parses files by file type and chunks their content.
    Supports .pdf, .md, .txt, and raw code files (.py, .cs, .cpp, .json, etc.).
    """
    def __init__(self, chunk_size: int = 1600, chunk_overlap: int = 200):
        self.splitter = RecursiveCharacterSplitter(chunk_size, chunk_overlap)
        # Supported plain text extensions
        self.text_extensions = {
            ".txt", ".md", ".py", ".cs", ".cpp", ".h", ".hpp", 
            ".json", ".js", ".ts", ".html", ".css", ".xml", ".yaml", ".yml", ".bat", ".ps1"
        }

    def parse(self, file_path: str) -> List[str]:
        """
        Parses a file and returns a list of text chunks.
        """
        if not os.path.exists(file_path):
            raise FileNotFoundError(f"File not found: {file_path}")

        _, ext = os.path.splitext(file_path)
        ext = ext.lower()

        try:
            if ext == ".pdf":
                return self._parse_pdf(file_path)
            elif ext in self.text_extensions:
                return self._parse_text(file_path)
            else:
                # Default fallback: try to parse as text if it's not a known binary
                return self._parse_text(file_path)
        except Exception as e:
            print(f"[-] Error parsing {file_path}: {e}")
            return []

    def _parse_pdf(self, file_path: str) -> List[str]:
        """
        Extracts text from PDF files page-by-page using PyMuPDF.
        """
        chunks = []
        doc = fitz.open(file_path)
        
        # We can extract text per page and chunk it page-by-page or combine.
        # Combining text and then recursively splitting is better because it prevents 
        # small disjointed page chunks.
        full_text = []
        for page_num in range(len(doc)):
            page = doc.load_page(page_num)
            text = page.get_text()
            if text:
                full_text.append(text)
                
        doc.close()
        
        combined_text = "\n\n".join(full_text)
        return self.splitter.split_text(combined_text)

    def _parse_text(self, file_path: str) -> List[str]:
        """
        Reads plain text files with robust encoding fallbacks and splits them.
        """
        encodings = ["utf-8", "utf-8-sig", "latin-1", "cp1252"]
        content = None
        
        for enc in encodings:
            try:
                with open(file_path, "r", encoding=enc, errors="replace") as f:
                    content = f.read()
                break
            except UnicodeDecodeError:
                continue
                
        if content is None:
            raise ValueError(f"Could not read text from {file_path} with any supported encoding.")
            
        return self.splitter.split_text(content)
