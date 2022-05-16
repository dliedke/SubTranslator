# SubTranslator

Automatically translate srt subtitles using Chrome and Selenium.

Better than some sites that translate the page because the translation quality is lost.

This takes more time but the translation quality is much better.

Usage: SubTranslator [originalLanguageCode] [translatedLanguageCode] [srt file or directory with srt files]

Example: SubTranslator en pt c:\temp\subtitle.srt

Language codes can be retrieve from Google Translator URL example: https://translate.google.com.br/?hl=en-US&sl=en&tl=pt&text=house%0A&op=translate

It is also possible to send a subdirectory as parameters to translate multiple .srt files in sequence.

In case there is a problem during the translation it is possible to just run again the program and it will resume the existing translation.
