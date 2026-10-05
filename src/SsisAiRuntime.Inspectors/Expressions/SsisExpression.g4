grammar SsisExpression;
options { language = CSharp; }
parse : expression EOF;
expression : conditional ('=' expression)?;
conditional : logicalOr ('?' expression ':' expression)?;
logicalOr : logicalAnd ('||' logicalAnd)*;
logicalAnd : bitwiseOr ('&&' bitwiseOr)*;
bitwiseOr : bitwiseXor ('|' bitwiseXor)*;
bitwiseXor : bitwiseAnd ('^' bitwiseAnd)*;
bitwiseAnd : equality ('&' equality)*;
equality : relational (('==' | '!=' | '<>') relational)*;
relational : shift (('<' | '<=' | '>' | '>=') shift)*;
shift : additive (('<<' | '>>') additive)*;
additive : multiplicative (('+' | '-') multiplicative)*;
multiplicative : unary (('*' | '/' | '%') unary)*;
unary : ('!' | '~' | '+' | '-') unary
      | '(' DATA_TYPE (',' INTEGER (',' INTEGER)?)? ')' unary
      | primary;
primary : STRING | NUMBER | INTEGER | HEX | BOOLEAN
        | WRAPPED_REFERENCE | UNWRAPPED_REFERENCE
        | IDENTIFIER '(' (expression (',' expression)*)? ')'
        | IDENTIFIER | COLUMN | '(' expression ')';
DATA_TYPE : 'DT_' ('BOOL' | 'BYTES' | 'CY' | 'DATE' | 'DBDATE' | 'DBTIME' | 'DBTIME2'
    | 'DBTIMESTAMP' | 'DBTIMESTAMP2' | 'DBTIMESTAMPOFFSET' | 'DECIMAL' | 'FILETIME'
    | 'GUID' | 'I1' | 'I2' | 'I4' | 'I8' | 'IMAGE' | 'NTEXT' | 'NUMERIC'
    | 'R4' | 'R8' | 'STR' | 'TEXT' | 'UI1' | 'UI2' | 'UI4' | 'UI8' | 'WSTR');
BOOLEAN : 'TRUE' | 'FALSE' | 'NULL';
WRAPPED_REFERENCE : '@[' ('\\]' | ~[\]\r\n])+ ']';
UNWRAPPED_REFERENCE : '@' [A-Za-z_] [A-Za-z0-9_]*;
COLUMN : '#' [0-9]+ | '[' ~[\]\r\n]+ ']';
IDENTIFIER : [A-Za-z_] [A-Za-z0-9_.$#]*;
HEX : '0x' [0-9A-Fa-f]+;
NUMBER : [0-9]+ '.' [0-9]* EXPONENT? | '.' [0-9]+ EXPONENT? | [0-9]+ EXPONENT;
INTEGER : [0-9]+;
fragment EXPONENT : [Ee] [+-]? [0-9]+;
STRING : '"' ('\\' . | '""' | ~["\\\r\n])* '"';
WHITESPACE : [ \t\r\n]+ -> channel(HIDDEN);